namespace BattlePvp.Logic
{
    public enum GameInputMode { Gameplay, Menu, TextInput, Spectating, Results }

    public static class InputModeRules
    {
        public static GameInputMode Resolve(bool typing, bool menu, bool dead, bool spectator, bool results)
        {
            if (typing) return GameInputMode.TextInput;
            if (results) return GameInputMode.Results;
            if (dead || spectator) return GameInputMode.Spectating;
            return menu ? GameInputMode.Menu : GameInputMode.Gameplay;
        }

        public static bool CanToggleMenu(GameInputMode mode) =>
            mode == GameInputMode.Gameplay || mode == GameInputMode.Menu;

        public static bool UsesFpsLook(string sceneName) => sceneName == "Battle" || sceneName == "Battle_waiting";

        // Both multiplayer scenes use FPS input. Menus and chat release the pointer.
        public static bool CanLockCursor(string sceneName, GameInputMode mode) =>
            UsesFpsLook(sceneName) && mode == GameInputMode.Gameplay;

        public static bool CanTrackCamera(GameInputMode mode, bool paused, bool typing) =>
            !typing && (!paused || mode == GameInputMode.Spectating);
    }

    public sealed class FrameInputGate
    {
        private int _escapeFrame = -1;
        private int _submitFrame = -1;
        public bool TryConsumeEscape(int frame)
        {
            if (_escapeFrame == frame) return false;
            _escapeFrame = frame;
            return true;
        }
        public void ConsumeSubmit(int frame) => _submitFrame = frame;
        public bool IsSubmitConsumed(int frame) => _submitFrame == frame;
        public void Clear() { _escapeFrame = _submitFrame = -1; }
    }
}
