using DVModManager.Models;
using Newtonsoft.Json;

namespace DVModProfiles.Profiles;

// How this mod reads the manager's JSON files.
internal static class ManagerJson
{
    // Nothing in here reads a profile's mod list: the selector lists names, and the sync moves
    // whole files around. Binding it anyway is what gives us the compile-time tie to the
    // manager's model, so tolerate a body we can't make sense of — an older mod against a newer
    // manager should still see the profiles rather than quietly sync none of them. A file that
    // isn't JSON at all still throws, and is still skipped as corrupt.
    public static readonly JsonSerializerSettings ProfileHeader = new()
    {
        // The manager writes LastModifiedAt as UTC; read anything without a zone as UTC too
        // rather than letting the local one creep in and make a profile look newer than it is.
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,

        Error = (_, args) =>
        {
            string? path = args.ErrorContext.Path;
            if (string.IsNullOrEmpty(path))
            {
                // Never got inside the object, so there's no header to salvage.
                return;
            }

            string member = path!.Split('.', '[')[0];
            if (member != nameof(ModProfile.Name) && member != nameof(ModProfile.LastModifiedAt))
            {
                args.ErrorContext.Handled = true;
            }
        },
    };
}
