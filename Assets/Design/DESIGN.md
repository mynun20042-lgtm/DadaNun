# Game Design

This document contains the design specifications for **Escape**, a cooperative 2D minigame designed for a local party game framework. It defines the sensory, aesthetic, and architectural intent of the game across UI Design, Asset Design, and Game Feedback Design to guide the developer in building a cohesive, highly polished experience.

### Core Overview
In **Escape**, multiple players control colored 2D circular pointers (tagged `playerpointer`) using their mobile joysticks (JoystickAB template). The core objective is cooperative: all active players must navigate a hazard-filled arena and reach a target escape zone simultaneously. The escape zone charges only when players occupy it, requiring shared movement coordination, obstacle avoidance, and collective survival.

- **Covered Domains:** UI Design, Asset Design, Game Feedback Design (Polishment)
- **Key Dependencies:** UI elements (specifically progress gauges and failure cards) directly depend on game state triggers (hazard collision, safe zone entrance). Asset glow elements are styled in tandem with the feedback flash and trail channels to maintain a high-contrast, cohesive cyber aesthetic.
- **Planning Note:** Prioritize the `(core)` assets and feedback interactions to establish a functional gameplay loop. Post-processing (specifically Bloom) is highly recommended but treated as a visual multiplier for the glow mechanics.

---

## UI Design

The user interface for Escape must feel integrated into the game world, reinforcing a high-tech, premium dark cyber aesthetic. It avoids generic flat layouts in favor of intentional depth layering and glowing state feedback.

### Color System
All UI colors are tinted toward the primary palette to maintain a premium feel. Pure, cold greys are strictly prohibited.
- **Background Base:** Deep Charcoal Black (#11131A) - grounds the viewport.
- **Surface Neutral (Cards/Panels):** Dark Slate Blue (#1C1E26) - used for standard background containers.
- **Surface Raised (Sub-panels/Active areas):** Midnight Navy (#262A36) - provides structural nesting.
- **Primary Accent (Cooperation, Success, Safety):** Cyber Neon Cyan (#00F0FF) - used for the escape progress, "ready" states, and safe areas.
- **Secondary Accent (Active elements, Highlights):** Electric Purple (#9D00FF) - used for interactive buttons, menu focus, and player indicators.
- **High-Stakes Accent (Danger, Defeat, Critical alerts):** Neon Hot Pink (#FF0055) - reserved exclusively for hazard indicators, damage, and defeat overlays.
- **Warm Accent (Warnings, Static info):** High-Voltage Yellow (#FFD700) - used for secondary stats and countdown warnings.

### Surface Hierarchy
Rather than using lines or borders, structural division is achieved through tonal shifts:
`Background Base (#11131A) -> Surface Neutral (#1C1E26) -> Surface Raised (#262A36)`
Each step up represents a visual layer closer to the viewer. Interactive elements feature a subtle gradient to simulate a physical, convex shape.

### Typography
The type scale uses dramatic proportions to emphasize gameplay numbers and maintain clear visibility from a distance (couch gaming).
- **Display/Headline Font:** Bold, high-tech, wide geometric sans-serif (conveys a cybernetic theme). Used for timer and critical alerts.
- **Body Font:** Modern, highly legible geometric sans-serif (neutral tone with wide tracking). Used for general instructions and menus.
- **Label Font:** Monospaced technical font. Used for player status, scores, and low-priority stats.

#### Typography Scale:
- **Display Large (Timer, Success Alert):** 72pt, Bold, All-Caps, Tracking +15% (Cyan or Pink depending on state).
- **Headline Medium (Lobby Title, Victory Header):** 36pt, Bold, All-Caps, Tracking +5% (Cyan or White).
- **Body Regular (Gameplay Tips, Rules):** 16pt, Medium Weight, Neutral White.
- **Technical Label (Player Name, Progress %):** 12pt, Monospace, Muted Steel Gray (#64748B).

### Layout & Depth
- **Depth Strategy:** The scene features three distinct layout depths. The play arena is the furthest depth layer. UI containers float above the arena. Overlays (Victory/Defeat) sit on the highest depth plane, completely isolating interaction when active.
- **Container Nesting:** Containers use a 16px corner rounding. Spacing follows a strict 8px grid (8px, 16px, 24px, 32px, 48px, 64px) for margins and padding. No thin dividers or borders are used. Spacing and tonal shifts differentiate data fields.
- **Asymmetry:** To break rigid box layouts, active panel cards feature an asymmetric accent tick (Neon Cyan) in the top-right corner, and decorative technical bracket icons bracket key headers.

### Components

#### 1. Action Buttons `(core)`
- **Shape:** Rounded capsules or rectangles with 12px corner radius.
- **Styling:** Linear gradient from top-left (Electric Purple #9D00FF) to bottom-right (Cyber Neon Cyan #00F0FF). Features a soft ambient drop shadow (5px blur) and a solid dark-purple bottom offset block (3px height) to simulate a physical, pressable keycap.
- **Hover/Selected State:** Button scales up uniformly to 104%. Emissive glow increases by 50%. Top-left gradient shifts toward a lighter violet.
- **Pressed State:** Translate button downward by 3px (overlapping the offset block to feel "depressed"). Scale drops to 98% and glow dims.

#### 2. Information Cards `(core)`
- **Shape:** Rectangular container with 16px corner radius.
- **Styling:** Solid Surface Neutral fill (#1C1E26). Smooth, soft ambient shadow (10px blur, black at 40% opacity). No borders. Generous 24px inner padding.

#### 3. Cooperative Escape Gauge `(core)`
- **Shape:** Thick, horizontal progress capsule.
- **Styling:** Inset appearance (fill background is sunken, using #0B0C10 with an inner shadow).
- **Fill Bar:** Cyber Neon Cyan (#00F0FF) with a soft horizontal gradient towards white at the leading edge to suggest energy accumulation. Features a persistent outer neon glow that grows stronger as the meter fills.

#### 4. Game Over / Victory Overlay Panels `(core)`
- **Shape:** Screen-wide modal panel.
- **Styling:** Semi-transparent dark background (#11131A at 85% opacity) to blur the play area. A central large Info Card pops up, displaying the result.
  - **Victory Overlay:** Features a glowing Neon Cyan halo around the card.
  - **Defeat Overlay:** Features a glowing Neon Hot Pink halo around the card.

#### 5. Connected Player Hub `(optional)`
- **Shape:** Small horizontal capsules lined up at the top-left of the screen.
- **Styling:** Shows player nicknames and active indicators. Filled with a player’s chosen neon color when they are inside the escape zone, and greyed out when outside.

---

## Asset Design

All visual assets must fit a modern, minimalist dark cyber theme. Objects are represented by simple, pure geometric shapes that rely on high-contrast colors and neon emissive properties rather than dense textures or complex models.

### Visual Identity
- **Art Style:** High-contrast vector-like 2D shapes with smooth radial and linear gradients.
- **Color Temperature:** Ultra-cold. Dominated by deep blues, violets, and indigos, pierced by bright neon highlights.
- **Detail Level:** Minimalist. Clear silhouettes and glowing edges are prioritized over internal details to ensure readability during fast-paced play.
- **Outline Treatment:** No dark outlines. Outlines, where present, are thin, glowing emissive lines that suggest holographic projection.

### Color Palette per Asset Category

| Category | Dominant Color | Secondary Color | Accent/Glow Color | Rationale |
|----------|----------------|-----------------|-------------------|-----------|
| **Player Pointers** `(core)` | Dynamic Player Tint | Soft Gradient White | Pure Neon Glow | High contrast for tracking individual movement |
| **Escape Zone** `(core)` | Cyber Neon Cyan (#00F0FF) | Translucent Cyan (15% opacity) | Glowing Cyan | Represents safety, charging state, and victory |
| **Hazards/Obstacles** `(core)`| Neon Hot Pink (#FF0055) | Dark Crimson (#4A001A) | Emissive Hot Pink | Immediate visual warning of danger |
| **Background Grid** `(optional)`| Deep Charcoal (#11131A) | Slate Blue (#1C1E26) | None (unlit) | Minimal distraction, aids speed/direction perception |

*Player Tint Selection:* Player 1 = Cyber Cyan (#00F0FF), Player 2 = Electric Purple (#BF55EC), Player 3 = Neon Pink (#FF2A6D), Player 4 = Solar Yellow (#F5D76E).

### Composition & Scale Rules
- **Silhouette Clarity:** Pointers are perfect circles. Hazards are sharp capsules or rapid-rotating spiked crosses. The escape zone is a massive, hollow concentric circle. This ensures instant readability.
- **Background Contrast:** Because the background is an unlit dark grid, all moving assets utilize self-illuminated emissive materials (or sprite shaders with emission) to float clearly above the arena.
- **Scale Relationships (Reference Viewport: 1920x1080):**
  - **Playable Field:** Bound within a 16:9 safe region.
  - **Player Pointer:** 48px diameter (1.0 unit in-world space). Fits comfortably inside hazards and safe zones.
  - **Escape Safe Zone:** 300px diameter (approx. 6.25 units in-world space). Intentionally sized to fit all 4 players simultaneously with room to dodge minor adjustments.
  - **Hazards/Obstacles:** Ranges from 32px to 96px width/height. Scale must feel threatening but allow tight navigation corridors (minimum 80px clearance between hazards).

### Reference
- **Existing Style Alignment:** The game matches the clean flat aesthetic of the existing dark premium theme (#11131A). It builds on the circular rendering concept of the `CircleSpriteFactory` in the `CoinGame`, but replaces flat fills with glowing, anti-aliased soft radial glow rings.

---

## Game Feedback Design (Polishment)

The feeling of "juice" in Escape is derived from extreme responsiveness, physical weight, and layering sensory feedback based on importance.

### Genre Profile
- **Tactical Arcade / High-Energy Hybrid:** Movement must feel fluid and responsive to mobile joystick inputs, but collisions and cooperative states must carry weight and clear indicator thresholds.

### Interaction Map

| Interaction | Tier | Importance | Camera | Time | Transform | Visual | Audio | Input | Rationale |
|---|---|---|---|---|---|---|---|---|---|
| **Player Movement** | Core | Minor | — | — | Slight stretch along velocity vector (max 1.1x length) | Fading colored emission trail (0.3s lifetime) | — | Continuous joystick rumble (low threshold, optional) | Communicates speed, velocity, and physical presence. |
| **Enter Safe Zone** | Core | Light | — | — | Subtle bounce scale (1.15x) on entry | 10x tiny cyan particle burst; safe zone border brightens | High-frequency soft synthesizer chime | Quick, sharp haptic pulse | Confirms safe registration for that player. |
| **Charging Escape** | Core | Medium | Subtle high-frequency vibration (only when charging) | — | Zone pulses slowly (sine-wave scale fluctuation) | Cyan energy connection beams link player pointer to center | Low hum pitching up with progress | — | Emphasizes team progress and tension of holding the zone. |
| **Collision with Hazard** | Core | Heavy | Directional kick shake (0.2s duration, exponential decay) | Hitstop freeze frame (0.04s on hit) | Pointer squashes flat against collision normal, then shatters | Hot Pink radial shard burst; screen flashes solid white (1 frame) | High-impact digital shattering wave | Extended heavy vibration pulse | Conveys critical failure and extreme danger. |
| **Escape Victory** | Core | Critical | Slow zoom-in towards the safe zone center | Slow-motion ramp (time scales to 0.1 over 2.0s) | UI scale overshoot (1.2x scale, settles at 1.0x over 0.5s) | Full-screen radial cyan shockwave; victory confetti rain | Uplifting major-key synth resolution chord with heavy reverb | Long, decaying rhythmic vibration | Celebrates successful coordination and completion. |
| **Escape Failure** | Core | Heavy | — | — | Pointer dissolves into dust | Screen-wide vignette dims and shifts to low saturation | Descending glitch sweep sound | Sharp double buzz | Signals failure clearly without being physically jarring. |
| **Near-Miss Avoidance** | Optional | Minor | — | — | — | Brief spark emitter between pointer and hazard edge | Soft synth "whoosh" sound | — | Rewards tight, high-skill evasive maneuvers. |

---

## Sequences

Multi-step events require precise timing to build anticipation, deliver the impact, and settle into the new state.

### 1. Collision & Defeat Sequence
```
Player Pointer collides with Hazard
  ├─ 0ms: Frame freezes entirely (Hitstop - 40ms duration). Pointer flashes solid, unshaded white.
  ├─ 40ms: Frame unfreezes. Pointer explodes into 12 high-velocity pink geometric shards.
  ├─ 40ms: Camera executes a sharp directional kick (10px amplitude, 0.2s exponential decay).
  ├─ 100ms: Screen vignette expands from corners, turning deep translucent Hot Pink (0.3s fade-in).
  ├─ 400ms: Level elements fade their emission to 20% intensity.
  └─ 800ms: Defeat Card scales down from top (1.2x -> 1.0x with an elastic overshoot easing over 0.4s).
```

### 2. Victory Celebration Sequence
```
Escape Progress reaches 100%
  ├─ 0ms: Escape Zone concentric rings rapidly merge into a solid white flash. Time scales down to 25% speed.
  ├─ 100ms: Emissive white ring expands outwards as a circular shockwave, fading at 2.0 units radius.
  ├─ 200ms: Safe Zone core begins a slow, rhythmic cyan pulse. Confetti particles (cyan and purple) begin raining from top.
  ├─ 600ms: Victory Overlay Card scales up from center (0.0x -> 1.1x -> 1.0x spring easing).
  ├─ 1000ms: Confetti color desaturates slightly to protect text readability.
  └─ 1200ms: Scoreboard / Escape Time numbers roll up rapidly with digital ticking SFX, settling on the final cooperative time.
```

---

## Required Visual & Audio Assets

### Art Assets

#### 1. Player Pointer Sprite `(core)`
- **Type:** 2D Sprite / Texture
- **Style:** Flat white circle with a dedicated alpha mask to render a soft, anti-aliased neon outer-glow ring.
- **In-Scene Size:** 48x48 pixels.

#### 2. Target Safe Zone Rings `(core)`
- **Type:** 2D Sprite / Texture
- **Style:** Concentric glowing lines with a translucent center (15% opacity). Styled to look like a futuristic docking pad.
- **In-Scene Size:** 512x512 pixels (scaled in-scene to 300x300 viewport equivalent).

#### 3. Capsule Hazard `(core)`
- **Type:** 2D Sprite / Texture
- **Style:** Sharp, capsule shape with a solid dark crimson core and an intense hot-pink glowing perimeter.
- **In-Scene Size:** 64x192 pixels.

#### 4. Grid Background Pattern `(optional)`
- **Type:** 2D Sprite (Tiled)
- **Style:** Ultra-thin 1px dark slate lines forming a repeating 64x64 grid pattern.
- **In-Scene Size:** 256x256 pixels (tiled across full screen).

#### 5. Spark/Shard Particle Texture `(optional)`
- **Type:** 2D Sprite / Texture
- **Style:** Simple sharp geometric diamond or shard with a solid white center and glowing cyan/pink edges.
- **In-Scene Size:** 16x16 pixels.

---

### Audio Assets

#### 1. Interface Audio Library `(core)`
- **Button Hover:** High-frequency, light digital "click" (100ms, crisp).
- **Button Press:** Low-frequency, deep mechanical "thud" with a short digital tail (250ms, heavy weight).

#### 2. Gameplay SFX Library `(core)`
- **Safe Zone Entry:** Uplifting synth chime in key of C major (0.5s, clean decay).
- **Charging Hum:** Seamless looping low-frequency electronic drone (starts at 100Hz, pitches up to 250Hz as charge increases).
- **Hazard Collision:** Harsh digital explosion/glass shattering sound (0.8s, high dynamic range).
- **Escape Victory:** Epic synth brass resolution chord with lush reverb and delay (2.5s duration).
- **Escape Failure:** Glitchy, low-frequency descending synth pitch drop (1.2s duration).
- **Near-Miss Swoosh:** Quick, high-pass filtered white noise sweep (0.3s, sharp panning). `(optional)`
