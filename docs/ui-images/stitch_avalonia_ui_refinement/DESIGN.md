---
name: Cosmos Network System
colors:
  surface: '#11131e'
  surface-dim: '#11131e'
  surface-bright: '#373845'
  surface-container-lowest: '#0b0e18'
  surface-container-low: '#191b26'
  surface-container: '#1d1f2b'
  surface-container-high: '#272935'
  surface-container-highest: '#323440'
  on-surface: '#e1e1f1'
  on-surface-variant: '#ccc3d3'
  inverse-surface: '#e1e1f1'
  inverse-on-surface: '#2e303c'
  outline: '#968e9c'
  outline-variant: '#4a4451'
  surface-tint: '#d7baff'
  primary: '#d7baff'
  on-primary: '#411478'
  primary-container: '#bd93f9'
  on-primary-container: '#4e2484'
  inverse-primary: '#714aaa'
  secondary: '#75d4e8'
  on-secondary: '#00363e'
  secondary-container: '#008092'
  on-secondary-container: '#f8fdff'
  tertiary: '#ffafd7'
  on-tertiary: '#620044'
  tertiary-container: '#fe78c5'
  on-tertiary-container: '#770054'
  error: '#ffb4ab'
  on-error: '#690005'
  error-container: '#93000a'
  on-error-container: '#ffdad6'
  primary-fixed: '#eddcff'
  primary-fixed-dim: '#d7baff'
  on-primary-fixed: '#290055'
  on-primary-fixed-variant: '#593090'
  secondary-fixed: '#a3eeff'
  secondary-fixed-dim: '#75d4e8'
  on-secondary-fixed: '#001f25'
  on-secondary-fixed-variant: '#004e5a'
  tertiary-fixed: '#ffd8e9'
  tertiary-fixed-dim: '#ffafd7'
  on-tertiary-fixed: '#3c0029'
  on-tertiary-fixed-variant: '#860f60'
  background: '#11131e'
  on-background: '#e1e1f1'
  surface-variant: '#323440'
typography:
  display-lg:
    fontFamily: Space Grotesk
    fontSize: 40px
    fontWeight: '700'
    lineHeight: 48px
    letterSpacing: -0.02em
  headline-md:
    fontFamily: Space Grotesk
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
  body-lg:
    fontFamily: Manrope
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  body-sm:
    fontFamily: Manrope
    fontSize: 14px
    fontWeight: '400'
    lineHeight: 20px
  label-caps:
    fontFamily: Space Grotesk
    fontSize: 12px
    fontWeight: '700'
    lineHeight: 16px
    letterSpacing: 0.05em
  mono-data:
    fontFamily: Space Grotesk
    fontSize: 14px
    fontWeight: '500'
    lineHeight: 20px
rounded:
  sm: 0.25rem
  DEFAULT: 0.5rem
  md: 0.75rem
  lg: 1rem
  xl: 1.5rem
  full: 9999px
spacing:
  unit: 4px
  xs: 4px
  sm: 8px
  md: 16px
  lg: 24px
  xl: 32px
  gutter: 16px
  margin_safe: 20px
---

## Brand & Style

The design system is engineered for high-performance network monitoring, blending technical precision with a premium, immersive aesthetic. It targets power users who require real-time data clarity without sacrificing visual sophistication.

The visual direction is **Glassmorphic-Modern**, drawing heavily from the "Deep Cosmos" palette. It utilizes depth through layered translucency, subtle luminosity, and high-contrast focal points. The interface evokes a sense of "observing the digital ether," utilizing vibrant accents against an expansive, dark background to prioritize information hierarchy and reduce cognitive load during extended monitoring sessions.

## Colors

The palette is rooted in a "Deep Cosmos" dark mode. The foundation is a midnight obsidian (`background_deep`), providing a high-contrast base for vibrant, neon-inspired accents.

- **Primary & Secondary:** A duo of electric violet and cyan, used for active states, data stream highlights, and primary call-to-actions.
- **Surface Strategy:** Instead of solid fills, surfaces use `surface_glass` with a background blur (20px-40px) to create the "Mica" effect.
- **Data Visualization:** Functional colors (Success, Warning, Error) are highly saturated to ensure they pop against the dark backgrounds, ensuring immediate recognition of network anomalies.

## Typography

This design system utilizes a dual-font strategy to balance technical utility with modern flair. 

**Space Grotesk** is used for headlines, labels, and numeric data. Its geometric terminals and technical rhythm align with the network monitoring theme, especially when displaying IP addresses or throughput speeds using tabular numbers.

**Manrope** serves as the primary reading typeface. Its balanced proportions and clean legibility ensure that logs and descriptive text remain comfortable to read on mobile displays.

## Layout & Spacing

The system follows a strict 4px soft-grid to ensure mathematical harmony across mobile screen sizes. 

- **Mobile Grid:** A fluid 4-column layout with 16px gutters and 20px side margins. 
- **Vertical Rhythm:** Content blocks are separated by 24px (lg) or 32px (xl) to allow the "glass" background effects to breathe.
- **Density:** High-density data views (like packet logs) may drop to 8px (sm) padding to maximize information density while maintaining touch targets of at least 44px.

## Elevation & Depth

Depth is conveyed through **Physicality and Translucency** rather than traditional drop shadows.

1.  **Backdrop Blur:** Use 32px Gaussian blurs on all container backgrounds.
2.  **Inner Strokes:** Elements utilize a 1px top-weighted inner border (`rgba(255, 255, 255, 0.1)`) to simulate a glass edge catching light.
3.  **Luminous Glows:** Interactive elements or active network nodes emit a soft, localized outer glow (8px - 16px radius) using their respective accent color at 20% opacity.
4.  **Z-Axis Hierarchy:**
    - Level 0: Solid Background (`background_deep`).
    - Level 1: Main Content Cards (65% opacity glass).
    - Level 2: Overlays, Modals, and Tooltips (85% opacity glass + 1px border).

## Shapes

The shape language is "Hyper-Softened Geometric." Standard containers and cards use a 1rem (16px) radius to feel modern and premium. 

Interactive components like buttons use a "Squircle" or pill-shape to differentiate them from static content containers. Smaller elements like tags or badges follow a slightly tighter 8px radius. The consistency in curvature balances the technical nature of the data with a friendly, high-end mobile feel.

## Components

### Buttons & Interaction
- **Primary:** Gradient fill (Primary to Secondary) with white text. High-gloss finish.
- **Secondary:** Ghost style with 1px primary-colored border and subtle backdrop blur.
- **Tactile Feedback:** On tap, buttons should scale down (0.96x) and increase their inner glow intensity.

### Data Visualization
- **Charts:** Line charts use "Glow-Paths"—thick lines (3pt) with a 10px outer glow of the same color. Areas under the line use a 10% opacity vertical gradient.
- **Gauges:** Circular indicators with segmented steps, utilizing the "Secondary" cyan for progress.

### Cards & Lists
- **Containers:** All cards must use `surface_glass` with an ultra-thin white border.
- **Lists:** Items separated by a 1px line (10% opacity) that does not extend to the edge of the card, creating a "floating" list effect.

### Input Fields
- **States:** Default is a dark, 20% opacity fill. Active state gains a 1px Primary color border and a subtle internal "focus" glow.

### Network Indicators
- **Pulse:** Active connections should feature a subtle "ping" animation—a scaling, fading ring emanating from the status dot.