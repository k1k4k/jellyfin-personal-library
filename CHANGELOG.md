# Changelog

What's new in each version, as shown in Jellyfin's plugin catalog.

## 0.1.1

- The "My library" button no longer shows over the video player.

## 0.1.0

- Each user chooses what they see in shared libraries, content type by content type (music, movies, shows…): everything, everything except what they hide, or only what they keep. One setting covers every library of the same type.
- "My library" window (button at the top, next to Search): mode per content type, search, browse folders, keep or hide an artist, an album, a folder, a movie, a show or a collection. Hidden content can still be found there to bring it back.
- "Hide from my library" / "Remove from my library" in the ⋮ menu of an item.
- Hiding goes through Jellyfin's own tag filter on the user profile: it applies everywhere (browsing, search, shuffle, instant mix, latest) and in every app, not only the web interface.
- New albums, movies or episodes covered by a choice (a kept artist or folder) follow automatically after each scan; tags wiped by a metadata refresh are put back.
- Admin page: who uses it, reset one user or everyone, turn self-service off. Uninstalling the plugin removes all its tags.
- If the administrator already restricts a user with "Allow items with tags", the "Only…" mode is not offered to them, so it can never widen that restriction.
