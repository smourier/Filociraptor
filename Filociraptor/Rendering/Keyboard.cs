namespace Filociraptor.Rendering;

// a mouse message carries no modifier of its own,
// so what shift and control are doing is read from the keyboard when the click is handled.
internal static class Keyboard
{
    public static bool IsShiftDown => IsDown(VIRTUAL_KEY.VK_SHIFT);
    public static bool IsControlDown => IsDown(VIRTUAL_KEY.VK_CONTROL);

    private static bool IsDown(VIRTUAL_KEY key) => Functions.GetKeyState((int)key) < 0;
}
