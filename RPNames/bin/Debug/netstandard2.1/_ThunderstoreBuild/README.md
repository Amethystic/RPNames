# RPNames

Give yourself a custom, animated, and colorful roleplaying title above your name! All titles are synced with other players in multiplayer.

![Mod Screenshot](https://cdn.discordapp.com/attachments/1415048760018337913/1428540073158250556/image.png?ex=68f2df1e&is=68f18d9e&hm=f494e32b9e513341ddee9bcc75d6e737bf048b62e020f4e292958f05fa7c1d8f&)
![Mod Gif](https://cdn.discordapp.com/attachments/1428470616079470672/1428542774768173176/ATLYSS_yO7pSs4zXT.gif?ex=68f2e1a2&is=68f19022&hm=ef5125b9b54185a5caff358dbf34b59d5af9609233f71b2ee581cce4651679a8&)
## Features
- **Fully In-Game GUI:** No more editing config files! Press **F8** (configurable) to open a stylish, animated menu and configure your title on the fly.
- **Per-Character Profiles:** Your title settings are saved for each character slot automatically.
- **Network Synced:** Other players with the mod (and CodeTalker) will see your custom title exactly as you designed it.

### Rich Customization Options:
- **Text Animations:**
    - **Static:** A simple, non-moving title.
    - **Scroll:** The title reveals itself letter by letter, then reverses.
    - **Marquee:** Your title scrolls across a set width, like a news ticker.
    - **Typewriter:** Types out the title, pauses with a blinking cursor, backspaces, and repeats.
- **Coloring Effects:**
    - **Single Color:** Pick any color using an in-game color picker or by entering a hex code.
    - **Gradient:** A smooth transition between two colors.
        - Can be static or animated to pulse between the start and end colors.
        - In-game color pickers for start and end colors.
        - Adjustable animation spread/speed.
    - **Rainbow:** The entire title cycles through the colors of the rainbow.
    - **Wave:** A beautiful rainbow wave effect that flows through the text.
    - **Static Rainbow:** Applies a fixed rainbow pattern across the text.
- **Formatting & Presets:**
    - Choose from over 10 different bracket styles (or none at all).
    - Position the title on a new line, with or without a gap.
    - Select from a list of classic preset titles to get started quickly.
- **Profile Management:**
    - **Copy/Paste:** Easily copy your current design and paste it into another character's profile.

## How to Use
1.  Once in-game with your character loaded, press the **F8** key to open the settings menu.
2.  Type your desired title in the text box at the top.
3.  Use the dropdown menus and sliders to customize the animation, coloring, and formatting.
4.  When you are happy with your title, click **"Set & Save Title"** at the bottom. Your title will be applied and saved for the current character.
5.  To remove a title, simply click the **"Clear Title"** button.

## Installation
- **Requires BepInEx.**
- Download the latest release and unzip it.
- Place `RPNames.dll` into your `BepInEx/plugins` folder.

## Dependencies
- **CodeTalker (Required for Multiplayer):** This mod relies on CodeTalker to send and receive title data between players.
    - If you are playing **single-player**, you do not need CodeTalker.
    - If you are playing **multiplayer**, every player who wants to see or show custom titles **must have both RPNames and CodeTalker installed.**

## Configuration
The mod's configuration file is located at `BepInEx/config/RPNames.cfg`.
- **Menu Key:** You can change the key used to open the menu (`F8` by default).
- **CharacterProfiles:** This stores all your profile data in JSON format. It is strongly recommended to **only edit your titles through the in-game menu** to avoid errors.