## Changelog
### 1.2.0
- **Massive Network Overhaul:** Replaced all network packets with highly efficient binary serialization to eliminate JSON overhead and drastically reduce packet size.
- **Bundled Syncing:** When a player joins a lobby, the host now sends all current player titles in a single, bundled packet. This massively reduces network traffic on player join.
- **Client-Side Animations:** Fixed the root cause of network spam. Animations are now handled 100% client-side. A single packet is sent only when a player changes their title settings.
- **Added Animation Speed Slider:** You can now control the speed of all text animations via a slider in the menu.
- **Fixed:** Corrected a critical compilation error by properly implementing a custom binary packet base class compatible with the latest CodeTalker API.
- **Fixed:** Corrected a logic error where title bracket styles (especially `None`) would not apply correctly for other players in the lobby.
- **Fixed:** Titles will now display correctly for players who do not have a `@GlobalNickname`.
- **Fixed:** Resolved a major bug that caused the custom GUI style to fail, preventing the menu from appearing or showing a default debug look. The menu is now stable and stylish!

### 1.0.0
- Initial release.