namespace DVModManager.Models;

/// <summary>Progress of a "Search on Nexus Mods" lookup for one mod.</summary>
public enum NexusSearchState
{
    Idle,
    Searching,
    Found,
    Uncertain,   // candidates exist, but none is safe to open on its own
    NotFound,
    Failed   // the request itself failed, so the result is unknown
}
