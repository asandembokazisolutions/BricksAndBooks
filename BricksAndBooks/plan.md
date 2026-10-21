# Bricks & Books site refinement plan

## Implementation approach

Preserve the existing ASP.NET Core MVC routes, controllers, authentication, residence data, and operational workflows. Improve the experience in the shared Razor shell and public homepage, then add a final CSS art-direction layer that resolves the existing cascade conflicts without rewriting the domain UI. Reuse supplied residence imagery and logos. Add a route manifest for the public MVC routes and keep all changes self-contained in the project.

## Project structure

- `Views/Shared/_Layout.cshtml`: shared navigation, public header, metadata, and authenticated shell.
- `Views/Home/Index.cshtml`: public discovery page and conversion flow.
- `wwwroot/css/site.css`: existing component styles plus a final, coherent visual system override.
- `wwwroot/js/site.js`: existing navigation, notification, and validation behavior; no business behavior changes.
- `wwwroot/images/`: supplied brand and residence imagery.
- `public/manus-routes.json`: route declaration for the MVC pages exposed by the site.

## Design direction

### Design movement

- Editorial campus hospitality with restrained Swiss-institutional structure (0.06 probability).
- Warm modern residence brochure with tactile paper tones and large photography (0.07 probability).
- Quiet operations console with teal signals and dense utility surfaces (0.04 probability).

Chosen: **Warm modern residence brochure**.

### Core principles

1. Lead with place and belonging before administration.
2. Use photography as the primary emotional signal, with generous but purposeful space.
3. Make every action obvious, calm, and accessible.
4. Keep operational surfaces legible and compact after the public landing experience.

### Color philosophy

Deep ink blue-green anchors trust and institutional clarity. A warm saffron accent adds welcome and wayfinding without feeling playful. Soft mineral canvas and cream surfaces keep the portal human and reduce the coldness of generic SaaS dashboards.

### Layout paradigm

Use an asymmetric editorial composition: a wide photographic hero with a floating search card, a staggered residence directory, and a narrow “how it works” rail. Avoid repetitive centered stacks and let photography create the page rhythm.

### Signature elements

- Saffron micro-labels and rules used as a recognizable Bricks & Books accent.
- Rounded photographic residence cards with quiet metadata chips.
- A floating search panel that visually bridges the hero and the directory.

### Interaction philosophy

Actions should feel like clear invitations rather than controls in a back-office system. Hover states lift cards slightly, buttons gain a warm shadow, focus states remain high-contrast, and mobile navigation stays simple.

### Animation

Use short 160–260ms transitions for hover, navigation, and card image scale. Avoid decorative motion on first paint. Respect `prefers-reduced-motion`.

### Typography system

Use Manrope for navigation, metadata, and UI copy. Use DM Serif Display for major editorial headings, residence names, and the primary promise. Keep body copy at a relaxed 1.6 line-height and use uppercase labels sparingly.

### Brand essence

A residence companion for UFS students and staff: find the right place, apply with confidence, and manage life after move-in. Personality: grounded, welcoming, dependable.

### Brand voice

Headlines are specific and reassuring. CTAs are direct and human.

- “Find your place at UFS.”
- “See what’s available, then take the next step.”

### Wordmark and logo

Keep the supplied Bricks & Books mark, but give it more breathing room and pair it with a compact wordmark lockup in the public header.

### Signature brand color

Saffron `#D69A3A`, used as the ownable wayfinding accent against deep ink `#102F38`.

## Scope boundaries

Do not change persistence, payment, Firebase, identity, controller behavior, or domain models as part of this visual refinement. The project currently cannot be compiled in this sandbox because the .NET SDK is unavailable; validate with static Razor/CSS inspection and leave the source ready for a normal .NET build.