using LmsCompanion.Application;
using LmsCompanion.Application.Abstractions;
using LmsCompanion.Application.Contracts;
using LmsCompanion.Application.Errors;
using LmsCompanion.Application.Services;
using LmsCompanion.Domain;

namespace LmsCompanion.UnitTests;

public sealed class CanvasOAuthFlowTests
{
    private static readonly AuthenticatedUserContext Owner = new(
        Guid.Parse("6d0c3926-cfc5-45df-a138-f5c4050d6ffb"),
        Guid.Parse("bff5434f-9ac3-4dc6-aa60-10b291e4a8fc"));
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly CanvasInstitutionConfiguration Institution = new(
        "north-campus",
        Owner.TenantId,
        new Uri("https://canvas.example.edu/"),
        "client-id",
        "client-secret",
        new Uri("https://companion.example.edu/api/v1/connections/canvas/callback"),
        ["url:GET|/api/v1/courses"]);

    [Fact]
    public async Task OAuth_state_is_hashed_bound_and_single_use_and_tokens_are_never_returned()
    {
        var fixture = new Fixture();
        var authorizationUri = await fixture.Flow.StartAsync(Owner, Institution.Id, CancellationToken.None);
        var rawState = System.Web.HttpUtility.ParseQueryString(authorizationUri.Query)["state"]!;

        Assert.Equal(1, fixture.States.AddCount);
        Assert.NotEqual(rawState, fixture.States.State!.StateHash);
        Assert.Equal(Owner.UserId, fixture.States.State.UserId);
        Assert.Equal(Owner.TenantId, fixture.States.State.TenantId);
        Assert.Equal(Institution.BaseUri.AbsoluteUri, fixture.States.State.BaseUrl);
        Assert.Contains("response_type=code", authorizationUri.Query, StringComparison.Ordinal);

        var result = await fixture.Flow.CompleteAsync("one-time-code", rawState, null, CancellationToken.None);

        Assert.Equal(LmsProviderType.Canvas, result.Provider);
        Assert.Equal(LmsConnectionStatus.Connected, result.Status);
        Assert.DoesNotContain("access-token", System.Text.Json.JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.Equal("access-token", fixture.Credentials.StoredAccessToken);
        Assert.Equal(Owner.UserId, fixture.Credentials.StoredUserId);
        Assert.Single(fixture.Connections.Items);
        Assert.Equal("canvas.connection.connected", fixture.Audit.Events.Last().EventType);
        await Assert.ThrowsAsync<ConnectionAccessDeniedException>(() => fixture.Flow.CompleteAsync(
            "one-time-code", rawState, null, CancellationToken.None));
    }

    [Fact]
    public async Task OAuth_start_rejects_inactive_membership_and_cross_tenant_institution()
    {
        var fixture = new Fixture();
        fixture.Memberships.Active = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Flow.StartAsync(
            Owner, Institution.Id, CancellationToken.None));
        Assert.Equal(0, fixture.States.AddCount);

        fixture.Memberships.Active = true;
        await Assert.ThrowsAsync<LmsProviderException>(() => fixture.Flow.StartAsync(
            new AuthenticatedUserContext(Owner.UserId, Guid.NewGuid()),
            Institution.Id,
            CancellationToken.None));
        Assert.Equal(0, fixture.States.AddCount);
    }

    [Fact]
    public async Task Provider_denial_consumes_state_without_exchanging_or_echoing_provider_details()
    {
        var fixture = new Fixture();
        var authorizationUri = await fixture.Flow.StartAsync(Owner, Institution.Id, CancellationToken.None);
        var state = System.Web.HttpUtility.ParseQueryString(authorizationUri.Query)["state"]!;

        await Assert.ThrowsAsync<CanvasAuthorizationDeniedException>(() => fixture.Flow.CompleteAsync(
            null, state, "access_denied", CancellationToken.None));

        Assert.Equal(0, fixture.OAuth.ExchangeCount);
        Assert.Equal("canvas.oauth.denied", fixture.Audit.Events.Last().EventType);
        await Assert.ThrowsAsync<ConnectionAccessDeniedException>(() => fixture.Flow.CompleteAsync(
            "code", state, null, CancellationToken.None));
    }

    [Fact]
    public async Task Callback_rejects_expired_state_before_exchanging_credentials()
    {
        var fixture = new Fixture();
        var uri = await fixture.Flow.StartAsync(Owner, Institution.Id, CancellationToken.None);
        var state = System.Web.HttpUtility.ParseQueryString(uri.Query)["state"]!;
        var saved = fixture.States.State!;
        await fixture.States.AddAsync(saved with { ExpiresAt = Now.AddSeconds(-1) }, CancellationToken.None);
        await Assert.ThrowsAsync<ConnectionAccessDeniedException>(() => fixture.Flow.CompleteAsync(
            "code", state, null, CancellationToken.None));
        Assert.Equal(0, fixture.OAuth.ExchangeCount);
        Assert.Empty(fixture.Connections.Items);
        Assert.Empty(fixture.Credentials.Credentials);
    }

    [Fact]
    public async Task Callback_rechecks_membership_and_consumes_state_when_access_was_removed()
    {
        var fixture = new Fixture();
        var uri = await fixture.Flow.StartAsync(Owner, Institution.Id, CancellationToken.None);
        var state = System.Web.HttpUtility.ParseQueryString(uri.Query)["state"]!;
        fixture.Memberships.Active = false;
        await Assert.ThrowsAsync<ConnectionAccessDeniedException>(() => fixture.Flow.CompleteAsync(
            "code", state, null, CancellationToken.None));
        fixture.Memberships.Active = true;
        await Assert.ThrowsAsync<ConnectionAccessDeniedException>(() => fixture.Flow.CompleteAsync(
            "code", state, null, CancellationToken.None));
        Assert.Equal(0, fixture.OAuth.ExchangeCount);
        Assert.Empty(fixture.Credentials.Credentials);
    }

    [Fact]
    public async Task Disconnect_rejects_foreign_owner_and_cleans_owned_connection_even_if_revocation_fails()
    {
        var fixture = new Fixture();
        var connection = new LmsConnection(
            Guid.NewGuid(), Owner.TenantId, Owner.UserId, LmsProviderType.Canvas,
            Institution.BaseUri.AbsoluteUri, "credential-reference", ["url:GET|/api/v1/courses"],
            LmsConnectionStatus.Connected, null);
        fixture.Connections.Items.Add(connection);
        fixture.Credentials.Credentials["credential-reference"] = new("access-token", "refresh-token");
        fixture.OAuth.RevokeResult = false;

        await Assert.ThrowsAsync<ConnectionAccessDeniedException>(() => fixture.Management.DisconnectAsync(
            new AuthenticatedUserContext(Guid.NewGuid(), Owner.TenantId), connection.Id, CancellationToken.None));
        Assert.Equal(0, fixture.OAuth.RevokeCount);
        Assert.Contains("credential-reference", fixture.Credentials.Credentials.Keys);

        await fixture.Management.DisconnectAsync(Owner, connection.Id, CancellationToken.None);

        var disconnected = Assert.Single(fixture.Connections.Items);
        Assert.Equal(LmsConnectionStatus.Disconnected, disconnected.Status);
        Assert.Null(disconnected.CredentialReference);
        Assert.DoesNotContain("credential-reference", fixture.Credentials.Credentials.Keys);
        Assert.Equal("lms.connection.disconnected", fixture.Audit.Events.Last().EventType);
    }

    [Fact]
    public async Task Expiring_canvas_access_token_is_refreshed_and_credential_is_replaced()
    {
        var fixture = new Fixture();
        var connection = new LmsConnection(
            Guid.NewGuid(), Owner.TenantId, Owner.UserId, LmsProviderType.Canvas,
            Institution.BaseUri.AbsoluteUri, "credential-reference", ["url:GET|/api/v1/courses"],
            LmsConnectionStatus.Connected, Now.AddMinutes(-1));
        fixture.Connections.Items.Add(connection);
        fixture.Credentials.Credentials["credential-reference"] = new("old-access", "refresh-token");

        var token = await fixture.TokenService.GetAccessTokenAsync(Owner, connection.Id, CancellationToken.None);

        Assert.Equal("refreshed-access", token);
        Assert.Equal(1, fixture.OAuth.RefreshCount);
        Assert.Equal(new ProviderCredential("refreshed-access", "refresh-token"),
            fixture.Credentials.Credentials["credential-reference"]);
        Assert.True(Assert.Single(fixture.Connections.Items).ExpiresAt > Now);
        Assert.Equal("canvas.token.refreshed", fixture.Audit.Events.Last().EventType);
    }

    [Fact]
    public async Task Expired_token_without_valid_refresh_marks_connection_for_reconnect()
    {
        var fixture = new Fixture();
        var connection = new LmsConnection(
            Guid.NewGuid(), Owner.TenantId, Owner.UserId, LmsProviderType.Canvas,
            Institution.BaseUri.AbsoluteUri, "credential-reference", ["url:GET|/api/v1/courses"],
            LmsConnectionStatus.Connected, Now.AddMinutes(-1));
        fixture.Connections.Items.Add(connection);
        fixture.Credentials.Credentials["credential-reference"] = new("expired-access", null);

        await Assert.ThrowsAsync<LmsProviderException>(() => fixture.TokenService.GetAccessTokenAsync(
            Owner, connection.Id, CancellationToken.None));

        Assert.Equal(LmsConnectionStatus.ReconnectRequired, Assert.Single(fixture.Connections.Items).Status);
        Assert.Equal("canvas.connection.reconnect_required", fixture.Audit.Events.Last().EventType);
        Assert.Equal(0, fixture.OAuth.RefreshCount);
    }

    [Fact]
    public async Task Disabling_institution_blocks_even_an_unexpired_stored_token()
    {
        var fixture = new Fixture();
        var connection = new LmsConnection(Guid.NewGuid(), Owner.TenantId, Owner.UserId,
            LmsProviderType.Canvas, Institution.BaseUri.AbsoluteUri, "credential-reference", [],
            LmsConnectionStatus.Connected, Now.AddHours(1));
        fixture.Connections.Items.Add(connection);
        fixture.Credentials.Credentials["credential-reference"] = new("valid-token", "refresh-token");
        fixture.Catalog.Enabled = false;
        await Assert.ThrowsAsync<LmsProviderException>(() => fixture.TokenService.GetAccessTokenAsync(
            Owner, connection.Id, CancellationToken.None));
        await Assert.ThrowsAsync<LmsProviderException>(() => fixture.Flow.StartAsync(
            Owner, Institution.Id, CancellationToken.None));
        Assert.Equal(0, fixture.OAuth.RefreshCount);
        Assert.Equal(0, fixture.States.AddCount);
    }

    private sealed class Fixture
    {
        public readonly TestMemberships Memberships = new();
        public readonly TestStates States = new();
        public readonly TestOAuth OAuth = new();
        public readonly TestCredentials Credentials = new();
        public readonly TestConnections Connections = new();
        public readonly TestAudit Audit = new();
        private readonly FixedTimeProvider _clock = new(Now);
        public readonly TestCatalog Catalog = new();

        public ICanvasOAuthFlow Flow => new CanvasOAuthFlow(
            Catalog, States, Memberships, OAuth, Credentials, Connections, Audit, _clock);
        public IConnectionManagementService Management => new ConnectionManagementService(
            Connections, Credentials, OAuth, Audit, _clock);
        public ICanvasAccessTokenService TokenService => new CanvasAccessTokenService(
            Connections, Credentials, Catalog, OAuth, Audit, _clock,
            new DisabledPersonalTokenPolicy());
    }

    private sealed class TestCatalog : ICanvasInstitutionCatalog
    {
        public bool Enabled { get; set; } = true;
        public IReadOnlyList<InstitutionSummaryDto> ListForTenant(Guid tenantId) =>
            tenantId == Institution.TenantId
                ? [new(Institution.Id, "North campus", "Canvas", Institution.BaseUri.AbsoluteUri, true)] : [];
        public CanvasInstitutionConfiguration? FindForTenant(string institutionId, Guid tenantId) =>
            Enabled && institutionId == Institution.Id && tenantId == Institution.TenantId ? Institution : null;
        public CanvasInstitutionConfiguration? FindForTenantByBaseUri(Uri baseUri, Guid tenantId) =>
            Enabled && tenantId == Institution.TenantId && baseUri.AbsoluteUri == Institution.BaseUri.AbsoluteUri ? Institution : null;
    }

    private sealed class DisabledPersonalTokenPolicy : IPersonalTokenPolicy { public bool Enabled => false; }

    private sealed class TestMemberships : ITenantMembershipRepository
    {
        public bool Active { get; set; } = true;
        public Task<AuthenticatedUserContext?> ResolveAsync(string externalSubject, Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<AuthenticatedUserContext?>(Active && externalSubject == "subject" && tenantId == Owner.TenantId ? Owner : null);
        public Task<bool> IsActiveAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult(Active && userId == Owner.UserId);
    }

    private sealed class TestStates : ICanvasOAuthStateRepository
    {
        public CanvasOAuthState? State { get; private set; }
        public int AddCount { get; private set; }
        public Task AddAsync(CanvasOAuthState state, CancellationToken cancellationToken)
        {
            State = state;
            AddCount++;
            return Task.CompletedTask;
        }
        public Task<CanvasOAuthState?> ConsumeAsync(string stateHash, DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (State is null || State.StateHash != stateHash || State.ExpiresAt <= now) return Task.FromResult<CanvasOAuthState?>(null);
            var consumed = State;
            State = null;
            return Task.FromResult<CanvasOAuthState?>(consumed);
        }
    }

    private sealed class TestOAuth : ICanvasOAuthClient
    {
        public int ExchangeCount { get; private set; }
        public int RevokeCount { get; private set; }
        public int RefreshCount { get; private set; }
        public bool RevokeResult { get; set; } = true;
        public Uri CreateAuthorizationUri(CanvasInstitutionConfiguration institution, string state) =>
            new($"{institution.BaseUri}login/oauth2/auth?state={Uri.EscapeDataString(state)}&response_type=code");
        public Task<CanvasTokenSet> ExchangeCodeAsync(CanvasInstitutionConfiguration institution, string code, CancellationToken cancellationToken)
        {
            ExchangeCount++;
            return Task.FromResult(new CanvasTokenSet("access-token", "refresh-token", Now.AddHours(1), []));
        }
        public Task<CanvasTokenSet> RefreshAsync(CanvasInstitutionConfiguration institution, string refreshToken, CancellationToken cancellationToken)
        {
            RefreshCount++;
            return Task.FromResult(new CanvasTokenSet("refreshed-access", null, Now.AddHours(1), []));
        }
        public Task<bool> RevokeAsync(Uri canvasBaseUri, string accessToken, CancellationToken cancellationToken)
        {
            RevokeCount++;
            return Task.FromResult(RevokeResult);
        }
    }

    private sealed class TestCredentials : ICredentialVault
    {
        public readonly Dictionary<string, ProviderCredential> Credentials = [];
        public string? StoredAccessToken { get; private set; }
        public Guid StoredUserId { get; private set; }
        public Task<string> StoreAsync(Guid tenantId, Guid userId, string accessToken, string? refreshToken, CancellationToken cancellationToken)
        {
            StoredAccessToken = accessToken;
            StoredUserId = userId;
            var reference = "credential-reference";
            Credentials[reference] = new(accessToken, refreshToken);
            return Task.FromResult(reference);
        }
        public Task<ProviderCredential?> RetrieveAsync(string reference, Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Credentials.GetValueOrDefault(reference));
        public Task ReplaceAsync(string reference, Guid tenantId, Guid userId, string accessToken, string? refreshToken, CancellationToken cancellationToken)
        {
            Credentials[reference] = new(accessToken, refreshToken);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string reference, Guid tenantId, Guid userId, CancellationToken cancellationToken)
        {
            Credentials.Remove(reference);
            return Task.CompletedTask;
        }
    }

    private sealed class TestConnections : ILmsConnectionRepository
    {
        public readonly List<LmsConnection> Items = [];
        public Task<IReadOnlyList<LmsConnection>> GetForUserAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LmsConnection>>(Items.Where(item => item.UserId == userId && item.TenantId == tenantId).ToArray());
        public Task<LmsConnection?> GetOwnedAsync(Guid connectionId, Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Id == connectionId && item.UserId == userId && item.TenantId == tenantId));
        public Task AddAsync(LmsConnection connection, CancellationToken cancellationToken)
        {
            Items.Add(connection);
            return Task.CompletedTask;
        }
        public Task UpdateAsync(LmsConnection connection, CancellationToken cancellationToken)
        {
            var index = Items.FindIndex(item => item.Id == connection.Id);
            if (index >= 0) Items[index] = connection;
            return Task.CompletedTask;
        }
    }

    private sealed class TestAudit : IAuditEventRepository
    {
        public readonly List<AuditEvent> Events = [];
        public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
