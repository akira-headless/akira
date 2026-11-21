using Microsoft.Extensions.Options;

namespace Akira.Infrastructure.Configuration;

public sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        var required = options.Provider switch
        {
            DatabaseProvider.Sqlite => new Dictionary<string, string>
            {
                [nameof(DatabaseOptions.SqlitePath)] = options.SqlitePath
            },
            DatabaseProvider.MariaDb => new Dictionary<string, string>
            {
                [nameof(DatabaseOptions.Host)] = options.Host,
                [nameof(DatabaseOptions.Name)] = options.Name,
                [nameof(DatabaseOptions.Username)] = options.Username,
                [nameof(DatabaseOptions.Password)] = options.Password
            },
            _ => null
        };

        if (required is null)
        {
            return ValidateOptionsResult.Fail(
                $"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.Provider)} '{options.Provider}' is not supported. " +
                $"Use one of: {string.Join(", ", Enum.GetNames<DatabaseProvider>())}.");
        }

        var failures = required
            .Where(setting => string.IsNullOrWhiteSpace(setting.Value))
            .Select(setting => $"{DatabaseOptions.SectionName}:{setting.Key} is required when " +
                               $"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.Provider)} is {options.Provider}.")
            .ToList();

        if (options.Provider == DatabaseProvider.MariaDb && options.Port is < 1 or > 65535)
        {
            failures.Add($"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.Port)} must be between 1 and 65535.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
