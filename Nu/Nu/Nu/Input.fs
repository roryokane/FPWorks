﻿namespace Nu
open OpenTK
open SDL2
open Nu

[<AutoOpen>]
module MouseButtonModule =

    /// Describes a mouse button.
    type [<StructuralEquality; StructuralComparison>] MouseButton =
        | MouseLeft
        | MouseCenter
        | MouseRight
        override this.ToString () =
            match this with
            | MouseLeft -> "Left"
            | MouseCenter -> "Center"
            | MouseRight -> "Right"

[<RequireQualifiedAccess>]
module MouseState =

    /// Convert a MouseButton to SDL's representation.
    let toSdlButton mouseButton =
        match mouseButton with
        | MouseLeft -> SDL.SDL_BUTTON_LEFT
        | MouseCenter -> SDL.SDL_BUTTON_MIDDLE
        | MouseRight -> SDL.SDL_BUTTON_RIGHT

    /// Convert SDL's representation of a mouse button to a MouseButton.
    let toNuButton mouseButton =
        match mouseButton with
        | SDL2.SDL.SDL_BUTTON_LEFT -> MouseLeft
        | SDL2.SDL.SDL_BUTTON_MIDDLE -> MouseCenter
        | SDL2.SDL.SDL_BUTTON_RIGHT -> MouseRight
        | _ -> failwith "Invalid SDL mouse button."

    /// Query that the given mouse button is down.
    let isButtonDown mouseButton =
        let sdlMouseButton = toSdlButton mouseButton
        SDL.SDL_BUTTON sdlMouseButton = 1u

    /// Get the position of the mouse.
    let getPosition () =
        let x = ref 0
        let y = ref 0
        ignore <| SDL.SDL_GetMouseState (x, y)
        Vector2I (!x, !y)

    /// Get the position of the mouse in floating-point coordinates.
    let getPositionF world =
        let mousePosition = getPosition world
        Vector2 (single mousePosition.X, single mousePosition.Y)

[<RequireQualifiedAccess>]
module KeyboardState =

    /// Query that the given keyboard key is down.
    let isKeyDown scanCode =
        let keyboardStatePtr = SDL.SDL_GetKeyboardState (ref 0)
        let keyboardStatePtr = NativeInterop.NativePtr.ofNativeInt keyboardStatePtr
        let state = NativeInterop.NativePtr.get<byte> keyboardStatePtr scanCode
        state = byte 1