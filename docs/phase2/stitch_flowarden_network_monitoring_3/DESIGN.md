---
name: Technical Forensic Console
colors:
  surface: '#141218'
  surface-dim: '#141218'
  surface-bright: '#3b383e'
  surface-container-lowest: '#0f0d13'
  surface-container-low: '#1d1b20'
  surface-container: '#211f24'
  surface-container-high: '#2b292f'
  surface-container-highest: '#36343a'
  on-surface: '#e6e0e9'
  on-surface-variant: '#cbc4d2'
  inverse-surface: '#e6e0e9'
  inverse-on-surface: '#322f35'
  outline: '#948e9c'
  outline-variant: '#494551'
  surface-tint: '#cfbcff'
  primary: '#cfbcff'
  on-primary: '#381e72'
  primary-container: '#6750a4'
  on-primary-container: '#e0d2ff'
  inverse-primary: '#6750a4'
  secondary: '#cdc0e9'
  on-secondary: '#342b4b'
  secondary-container: '#4d4465'
  on-secondary-container: '#bfb2da'
  tertiary: '#e7c365'
  on-tertiary: '#3e2e00'
  tertiary-container: '#c9a74d'
  on-tertiary-container: '#503d00'
  error: '#ffb4ab'
  on-error: '#690005'
  error-container: '#93000a'
  on-error-container: '#ffdad6'
  primary-fixed: '#e9ddff'
  primary-fixed-dim: '#cfbcff'
  on-primary-fixed: '#22005d'
  on-primary-fixed-variant: '#4f378a'
  secondary-fixed: '#e9ddff'
  secondary-fixed-dim: '#cdc0e9'
  on-secondary-fixed: '#1f1635'
  on-secondary-fixed-variant: '#4b4263'
  tertiary-fixed: '#ffdf93'
  tertiary-fixed-dim: '#e7c365'
  on-tertiary-fixed: '#241a00'
  on-tertiary-fixed-variant: '#594400'
  background: '#141218'
  on-background: '#e6e0e9'
  surface-variant: '#36343a'
typography:
  h1:
    fontFamily: geist
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
  h2:
    fontFamily: geist
    fontSize: 20px
    fontWeight: '600'
    lineHeight: 28px
  h3:
    fontFamily: geist
    fontSize: 16px
    fontWeight: '600'
    lineHeight: 24px
  body-lg:
    fontFamily: geist
    fontSize: 15px
    fontWeight: '400'
    lineHeight: 22px
  body-md:
    fontFamily: geist
    fontSize: 14px
    fontWeight: '400'
    lineHeight: 20px
  body-sm:
    fontFamily: geist
    fontSize: 13px
    fontWeight: '400'
    lineHeight: 18px
  data-mono:
    fontFamily: jetbrainsMono
    fontSize: 13px
    fontWeight: '450'
    lineHeight: 18px
    letterSpacing: -0.01em
  label-caps:
    fontFamily: geist
    fontSize: 11px
    fontWeight: '600'
    lineHeight: 16px
    letterSpacing: 0.05em
rounded:
  sm: 0.125rem
  DEFAULT: 0.25rem
  md: 0.375rem
  lg: 0.5rem
  xl: 0.75rem
  full: 9999px
spacing:
  page_padding: 24px
  element_gap: 16px
  group_gap: 8px
  control_height_md: 32px
  control_height_lg: 36px
  table_row_height: 32px
---

## Brand & Style

This design system is engineered for high-stakes network observation and digital forensics. The aesthetic is rooted in "Technical Minimalism"—a style that prioritizes data density and clarity over decorative elements. It draws inspiration from Semi Design and industrial monitoring consoles, utilizing a structured, modular approach to information architecture.

The tone is calm, authoritative, and precise. It avoids the "gamer" aesthetic often associated with dark modes, instead opting for a sophisticated, workstation-like environment. The visual language uses thin borders and distinct tonal shifts to separate concerns, ensuring that even when the screen is saturated with packet data, the user remains focused and un-fatigued. There are no gradients, no soft shadows, and no organic "pill" shapes; every element is rectilinear and intentional.

## Colors

The palette is strictly functional, utilizing a deep neutral foundation to minimize eye strain during extended monitoring sessions. 

- **Foundation:** The hierarchy is built on three levels of darkness: the deep background (#0F1117), the standard surface for widgets/panels (#181B23), and a raised surface for interactive elements or active states (#20242E).
- **Accents:** Color is used exclusively as a data carrier. **Cyan** identifies active traffic and outbound flow. **Green** signifies system health and verified connections. **Amber** and **Red** are reserved for state-based warnings and critical errors, respectively. **Purple** is utilized as a secondary differentiator in complex time-series charts to prevent visual overlap with traffic data.
- **Borders:** A consistent #2B303B border is the primary tool for structural separation, replacing the need for shadows.

## Typography

This design system utilizes a dual-font strategy to balance readability and technical utility. 

**Geist** serves as the primary sans-serif for the interface, chosen for its neutral, mechanical precision and exceptional legibility in dark environments. It is used for all UI headings, body text, and navigational elements.

**JetBrains Mono** is employed for all variable data, including IP addresses, MAC addresses, port numbers, and packet payloads. Its monospaced nature ensures that columns of numerical data align perfectly, allowing for rapid scanning of anomalies. Use `label-caps` for table headers and section titles to provide a clear structural hierarchy.

## Layout & Spacing

The layout philosophy is centered on a high-density "Console" model. Information is organized into modular panels that utilize a fluid grid system, allowing the interface to scale from laptop screens to large-format monitoring walls.

- **Grid:** A 12-column grid system with 16px gutters. Panels should typically span 3, 4, 6, or 12 columns.
- **Rhythm:** A strict 4px/8px baseline is maintained. Page containers use a 24px internal padding to create breathing room against the screen edges.
- **Density:** Control heights are kept between 32px and 36px to maximize the amount of visible data. Tables use a 32px row height, striking a balance between information density and hit-target accessibility.

## Elevation & Depth

Depth in this design system is achieved through "Tonal Stacking" rather than physical metaphors like shadows or blurs. 

1.  **Floor (Level 0):** The #0F1117 background acts as the canvas.
2.  **Panels (Level 1):** Widgets and content areas use #181B23. They are separated from the background by a 1px border (#2B303B).
3.  **Active/Raised (Level 2):** Hover states, active tabs, and modal interiors use #20242E. 
4.  **Floating Elements:** Tooltips and context menus use #20242E with a slightly brighter border (#3F444E) to ensure visibility against the background layers. 

Avoid using shadows entirely to maintain the crisp, technical appearance required for professional monitoring tools.

## Shapes

The shape language is strictly geometric and disciplined. A 6px corner radius is the standard for almost all UI components, including buttons, input fields, and panel corners. This provides a "softened-technical" feel—cleaner than sharp 90-degree angles but more professional than highly rounded shapes.

Larger containers or outer frames may use an 8px radius to create a subtle nested effect. Pill shapes (fully rounded ends) are prohibited, even for tags or chips, to maintain the "Specialized Console" tone.

## Components

- **Buttons:** Height set to 32px. Primary buttons use a solid Cyan fill with black text. Secondary buttons use a transparent fill with a #2B303B border. Ghost buttons are reserved for low-priority toolbar actions.
- **Data Tables:** The core of the system. Use #181B23 for rows with no zebra striping. Instead, use a subtle 1px bottom border (#2B303B). Text should be JetBrains Mono for data columns.
- **Status Indicators:** Small 8px squares or subtle 2px vertical bars. Use the Green/Amber/Red palette. Avoid large glowing effects; a simple flat fill is preferred.
- **Inputs:** 32px height, #0F1117 background, and #2B303B border. Upon focus, the border changes to Cyan.
- **Chips/Tags:** Rectangular with a 4px radius. Use a subtle background tint (e.g., Cyan at 10% opacity) with a matching colored border and text for category labeling.
- **Charts:** Line charts should use a 1.5px stroke width. Primary traffic is Cyan; secondary or historic data is Purple. Grid lines should be #2B303B.
- **Side Navigation:** Narrow 64px collapsed or 240px expanded. Uses the #181B23 surface with an active indicator consisting of a 3px Cyan vertical bar on the leading edge.