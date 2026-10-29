// The player's controls, bound as Myth binds them (Controls.cs): arrows or WASD to move, Space to
// jump, E to attack, Shift or F to dash, X held to zoom in; on a gamepad the left stick or d-pad,
// South, West, the right trigger and a click of the left stick. A stick reaches full speed at three quarters of its travel (ShapeMoveX).

control_move_x :: () -> float {
    keys := cast(float) (cast(s32) (key_down(SDLK_RIGHT) || key_down(SDLK_D)) - cast(s32) (key_down(SDLK_LEFT) || key_down(SDLK_A)));
    if keys return keys;
    if !gamepad return 0;
    dpad := cast(s32) button_down(.DPAD_RIGHT) - cast(s32) button_down(.DPAD_LEFT);
    if dpad return xx dpad;
    stick := past_deadzone(axis(.LEFTX), MOVE_STICK_DEADZONE);
    return clamp(stick / ((MOVE_STICK_FULL_SPEED - MOVE_STICK_DEADZONE) / (1 - MOVE_STICK_DEADZONE)), -1, 1);
}

control_move_y :: () -> s32 {
    keys := cast(s32) (key_down(SDLK_DOWN) || key_down(SDLK_S)) - cast(s32) (key_down(SDLK_UP) || key_down(SDLK_W));
    if keys || !gamepad return keys;
    dpad := cast(s32) button_down(.DPAD_DOWN) - cast(s32) button_down(.DPAD_UP);
    if dpad return dpad;
    y := axis(.LEFTY);
    return cast(s32) (y > 0.5) - cast(s32) (y < -0.5);
}

control_jump_pressed :: () -> bool { return key_pressed(SDLK_SPACE) || button_pressed(.SOUTH); }
control_jump_down :: () -> bool { return key_down(SDLK_SPACE) || button_down(.SOUTH); }
control_attack_pressed :: () -> bool { return key_pressed(SDLK_E) || button_pressed(.WEST); }
control_dash_pressed :: () -> bool { return key_pressed(SDLK_LSHIFT) || key_pressed(SDLK_F) || trigger_pressed; }
control_dash_down :: () -> bool { return key_down(SDLK_LSHIFT) || key_down(SDLK_F) || trigger_down; }
// The right stick, to look ahead.
control_look :: () -> Vector2 {
    if !gamepad return .{};
    return .{past_deadzone(axis(.RIGHTX), 0.25), past_deadzone(axis(.RIGHTY), 0.25)};
}

control_zoom_down :: () -> bool { return key_down(SDLK_X) || gamepad && button_down(.LEFT_STICK); }

// Each frame, after the events: what the trigger did since the last.
update_controls :: () {
    was := trigger_down;
    trigger_down = gamepad && axis(.RIGHT_TRIGGER) >= 0.25;
    trigger_pressed = trigger_down && !was;
}

// From the event loop.
control_event :: (event: SDL_Event) {
    if event.type == xx SDL_EventType.GAMEPAD_ADDED && !gamepad gamepad = SDL_OpenGamepad(event.gdevice.which);
    if event.type == xx SDL_EventType.GAMEPAD_REMOVED && gamepad && SDL_GetGamepadID(gamepad) == event.gdevice.which { SDL_CloseGamepad(gamepad); gamepad = null; }
    if event.type == xx SDL_EventType.GAMEPAD_BUTTON_DOWN array_add(*buttons_pressed, cast(SDL_GamepadButton) event.gbutton.button);
}

begin_control_frame :: () { buttons_pressed.count = 0; }

#scope_file

MOVE_STICK_DEADZONE :: 0.25;
MOVE_STICK_FULL_SPEED :: 0.75;

gamepad: *SDL_Gamepad;
buttons_pressed: [..] SDL_GamepadButton;
trigger_down, trigger_pressed: bool;

button_down :: (button: SDL_GamepadButton) -> bool { return SDL_GetGamepadButton(gamepad, button); }
button_pressed :: (button: SDL_GamepadButton) -> bool { return array_find(buttons_pressed, button); }
axis :: (a: SDL_GamepadAxis) -> float { return SDL_GetGamepadAxis(gamepad, a) / 32767.0; }
past_deadzone :: (v: float, deadzone: float) -> float { return ifx abs(v) <= deadzone then 0.0 else sign(v) * (abs(v) - deadzone) / (1 - deadzone); }

#import "Basic";
#import "Math";
#import "SDL3";
#import "Kit";
