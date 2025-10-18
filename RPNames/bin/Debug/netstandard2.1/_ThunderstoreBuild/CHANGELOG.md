## Changelog
### 1.2.1
- **Fixed:** Resolved a critical bug that caused the game to hang on the character screen when leaving one server and joining another. The mod's network state is now correctly reset on disconnect.
- **Fixed:** The mod's custom UI style no longer "bleeds" into the main menu or other UIs after disconnecting from a server.
- **Fixed:** Titles now display correctly for players who do not have a `@GlobalNickname` enabled.
- **Fixed:** Local player's title color now loads correctly on entering the game, instead of appearing black until re-saved.
- **Added:** A "Color Speed" slider to control the animation speed for Rainbow, Wave, and animated Gradient effects.

### 1.2.0
- **Massive Network Overhaul:** Replaced all network packets with highly efficient binary serialization to eliminate JSON overhead and drastically reduce packet size.
- **Bundled Syncing & Client-Side Animations:** Reworked the entire networking model to eliminate packet spam and reduce traffic on player join.
- **Added Animation Speed Slider:** You can now control the speed of all text animations.

### 1.0.0
- Initial release.