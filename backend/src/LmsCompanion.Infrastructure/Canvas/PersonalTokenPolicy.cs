using LmsCompanion.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace LmsCompanion.Infrastructure.Canvas;

public sealed class PersonalTokenPolicy(IConfiguration configuration) : IPersonalTokenPolicy
{
    public bool Enabled => configuration.GetValue<bool>("Canvas:AllowPersonalTokenTesting");
}
