# Architecture

Termina uses a reactive MVVM architecture with declarative layouts and surgical region-based rendering.

## Overview

```
┌─────────────────────────────────────────────────────────────┐
│                        Application                          │
├─────────────────────────────────────────────────────────────┤
│  ┌─────────────┐    ┌──────────────┐    ┌───────────────┐  │
│  │  ViewModel  │───▶│    Page      │───▶│  Layout Tree  │  │
│  │ Input,State │    │ Focus,Layout │    │  (ILayoutNode)│  │
│  └─────────────┘    └──────────────┘    └───────────────┘  │
│         ▲                  │                    │          │
│         │                  ▼                    ▼          │
│  ┌─────────────┐    ┌──────────────┐    ┌───────────────┐  │
│  │   Keyboard  │    │    Focus     │    │   Renderer    │  │
│  │    Input    │    │   Manager    │    │  (ANSI out)   │  │
│  └─────────────┘    └──────────────┘    └───────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

## Design Principles

### 1. Separation of Concerns

- **ViewModels** own state, handle business logic, and expose observable properties
- **Pages** own focus management, key bindings, navigation, build layouts from ViewModel state, and manage interactive layout nodes
- **Layout Nodes** render to terminal and may handle routed input when focused

### 2. Reactive by Default

State changes automatically propagate to the UI:

```csharp
// ViewModel changes state
Count++;  // Sets Count, emits to CountChanged

// Page automatically updates
ViewModel.CountChanged
    .Select(c => new TextNode($"Count: {c}"))
    .AsLayout()  // Subscribes and updates on change
```

### 3. Declarative Layouts

UI is described as a tree, not imperatively drawn:

```csharp
return Layouts.Vertical()
    .WithChild(header.Height(1))
    .WithChild(content.Fill())
    .WithChild(footer.Height(1));
```

### 4. Region-Based Rendering

Only changed regions are re-rendered, not the entire screen. This provides:
- Smooth updates without flicker
- Efficient use of terminal bandwidth
- Better performance for streaming content

## Component Lifecycle

### Application Start

1. Host builds and starts
2. Router matches initial route
3. Page/ViewModel pair created via DI
4. ViewModel's `OnActivated()` called
5. Page's `BuildLayout()` called
6. Initial render to terminal

### On Input

Termina uses a two-phase input routing model (similar to DOM event handling):

1. Key press captured by application
2. **Capture Phase**: Page's `KeyBindings` checked first
   - If matched, handler executes (e.g., `Navigate("/menu")`)
   - Key is consumed, stops here
3. **Bubble Phase**: If not consumed, `FocusManager` routes to focused component
   - Focused `IFocusable` nodes (TextInput, SelectionList) handle input
   - If component returns `true`, key is consumed
4. **ViewModel Phase**: Unconsumed input sent to ViewModel's `Input` observable
   - ViewModel can subscribe for fallback handling
5. Reactive bindings emit new values from state changes
6. Affected layout nodes invalidate
7. Changed regions re-render

This model ensures navigation keys (Escape, Tab) work reliably while allowing focused components to handle their own input.

### Invalidation

A layout node tells the page that it must render again through its `Invalidated` stream.
Container and wrapper nodes forward the `Invalidated` stream of their children to their own `Invalidated` stream.
These nodes are `StackLayout`, `VerticalLayout`, `HorizontalLayout`, `PanelNode`, `ScrollableContainerNode`, `GridNode`, `DynamicLayoutNode`, `KeyedDynamicLayoutNode`, `ReactiveLayoutNode`, and `WizardNode`.
The page subscribes to the stream of the layout root, and requests a redraw for each emission.

A parent node subscribes to the streams of its children only while something subscribes to its own stream:

- The first subscriber connects the parent node to all of its children.
- A child that you add to an observed parent node is subscribed at once.
- A child that you remove or replace is released at once.
- When the last subscriber leaves, the parent node releases all of its child subscriptions.
- `Dispose()` releases the child subscriptions and completes the stream.

Activation does not change the forwarding.
`OnActivate()` and `OnDeactivate()` start and stop the children, for example timers and animations.
An active parent node activates a child that you add or that replaces the old content.

When the page replaces its layout root, it releases its subscription to the old root.
Thus the old tree releases its child subscriptions, and a child that the new tree reuses does not keep the old parent nodes alive.
For example, a reactive layout factory can put the same stateful node into a new container for each value.

### On Navigation

**ResetOnNavigation (default):**
1. `Navigate("/path")` called
2. Current ViewModel's `OnDeactivating()` called
3. Current Page's `OnNavigatingFrom()` called (deactivates layout)
4. New Page/ViewModel created
5. New ViewModel's `OnActivated()` called
6. New Page's `OnNavigatedTo()` called (builds and activates layout)
7. Full render

**PreserveState:**
1. `Navigate("/path")` called
2. Current ViewModel's `OnDeactivating()` called (disposes subscriptions)
3. Current Page's `OnNavigatingFrom()` called (calls `OnDeactivate()` on layout tree)
4. Cached Page/ViewModel retrieved (or created on first visit)
5. ViewModel's `OnActivated()` called (recreates subscriptions)
6. Page's `OnNavigatedTo()` called (calls `OnActivate()` on layout tree)
7. Render (layout preserved, just reactivated)

## The Rendering Pipeline

```
BuildLayout() → Measure() → Render()
      │             │            │
      ▼             ▼            ▼
   Tree of      Calculate    Write ANSI
  ILayoutNode    sizes       to stdout
```

### 1. BuildLayout

Creates the layout tree describing the UI structure.

### 2. Measure

Starting from root, each node calculates its desired size given available space. Two-pass algorithm for container nodes:
1. Measure fixed/auto children
2. Distribute remaining space to fill children

### 3. Render

Each node renders to its allocated bounds using ANSI escape sequences.

## Why No Spectre.Console?

Termina V3 renders directly via ANSI escape sequences instead of using Spectre.Console. Benefits:

1. **Surgical Updates** - Re-render only changed regions
2. **True Streaming** - Character-by-character updates
3. **Full Control** - Custom rendering optimization
4. **AOT Compatible** - No reflection-based layout

## Source Code

::: details View ReactiveViewModel implementation
<<< @/../src/Termina/Reactive/ReactiveViewModel.cs{csharp}
:::
