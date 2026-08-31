namespace Filociraptor.Configuration;

// source generated, so the settings serialise with no reflection at all,
// and survive trimming and ahead of time compilation.
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(RecentFolder))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}
