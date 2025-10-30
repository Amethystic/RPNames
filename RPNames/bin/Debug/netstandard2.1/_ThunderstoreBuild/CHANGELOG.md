## Changelog
## 1.3.2 - x.x.3
- **UI:** Title/Pronoun limit is now 32 -> 50 Chars max.

### 1.3 - x.x.1
- **Feature:** Added a dedicated Pronouns system. You can now add pronouns and select a bracket style for them in the settings menu, separate from the main title.
- **Fixed:** Resolved a critical desynchronization bug that caused player titles to randomly not appear for other players. The fix ensures title visibility is consistent for everyone in the lobby.
- **UI:** Minor layout improvements to the settings menu for clarity.
- **Feature:** Added full customization for pronouns. They can now have their own unique coloring, gradients, and animated effects, independent of the main title.
- **Feature:** Added a "Share Title Coloring" option for pronouns for easy color matching.
- **UI:** The settings menu has been reorganized with collapsible sections and a scrollbar to accommodate the new options without clutter.
- **Fixed:** (NOT RLLY A FIX I ONLY JUST TOUCHED THE README A LITTLE BIT YOU CAN IGNORE THIS LINE)

### 1.2.2
- **Fixed:** Resolved a bug that caused player titles to disappear for the host when changing maps.
- **Fixed:** Resolved a bug that caused the animation titles going crazy if mixed with rainbow/gradient animations.

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