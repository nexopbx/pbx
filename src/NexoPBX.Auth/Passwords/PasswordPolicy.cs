// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.Extensions.Options;

namespace NexoPBX.Auth.Passwords;

/// <summary>Decides whether a new password is acceptable. Length matters more than composition rules.</summary>
public sealed class PasswordPolicy
{
    private readonly IOptions<AuthOptions> _options;

    /// <summary>Creates the policy.</summary>
    public PasswordPolicy(IOptions<AuthOptions> options) => _options = options;

    /// <summary>Throws <see cref="NexoValidationException"/> if the password is not acceptable.</summary>
    public void EnsureAcceptable(string password, string username)
    {
        var o = _options.Value;
        if (string.IsNullOrEmpty(password) || password.Length < o.MinPasswordLength)
        {
            throw new NexoValidationException("password", $"Password must be at least {o.MinPasswordLength} characters long.");
        }

        if (password.Length > o.MaxPasswordLength)
        {
            throw new NexoValidationException("password", $"Password must be at most {o.MaxPasswordLength} characters long.");
        }

        if (password.Distinct().Count() < 4)
        {
            throw new NexoValidationException("password", "Password is too repetitive.");
        }

        if (password.Contains(username, StringComparison.OrdinalIgnoreCase))
        {
            throw new NexoValidationException("password", "Password must not contain the username.");
        }
    }
}
