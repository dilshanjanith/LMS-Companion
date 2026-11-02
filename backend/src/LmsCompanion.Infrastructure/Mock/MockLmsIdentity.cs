namespace LmsCompanion.Infrastructure.Mock;

/// <summary>Non-secret identity values for the local mock account only.</summary>
public static class MockLmsIdentity
{
    public static readonly Guid TenantId = Guid.Parse("7e8c6a81-7116-4d80-a8c2-550b94148ad1");
    public static readonly Guid UserId = Guid.Parse("f4a538ad-d409-44e5-af5d-50f1373a4620");
    public static readonly Guid ConnectionId = Guid.Parse("2e0b09f7-8bdf-40b8-a8ec-88e5eaabf5ef");
}
