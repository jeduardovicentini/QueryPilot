# Helpers Win32 para automação de testes do SSMS (dot-source este arquivo).
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class SsmsWin {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    public static string Text(IntPtr h) { var s = new StringBuilder(4096); GetWindowText(h, s, s.Capacity); return s.ToString(); }
    public static string Cls(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, s.Capacity); return s.ToString(); }
    public static List<IntPtr> Top(uint pid) { var r = new List<IntPtr>(); EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) r.Add(h); return true; }, IntPtr.Zero); return r; }
    public static List<IntPtr> Kids(IntPtr parent) { var r = new List<IntPtr>(); EnumChildWindows(parent, (h, l) => { r.Add(h); return true; }, IntPtr.Zero); return r; }
    // Clica no botão <button> de qualquer janela do processo que contenha <text>. Retorna true se clicou.
    public static bool ClickIfDialog(uint pid, string text, string button) {
        foreach (var top in Top(pid)) {
            var kids = Kids(top);
            bool match = false;
            foreach (var k in kids) if (Text(k).Contains(text)) { match = true; break; }
            if (!match) continue;
            foreach (var k in kids) {
                if (Cls(k).IndexOf("button", StringComparison.OrdinalIgnoreCase) >= 0 && Text(k).Replace("&", "") == button) {
                    SendMessage(k, 0x00F5, IntPtr.Zero, IntPtr.Zero); // BM_CLICK
                    return true;
                }
            }
        }
        return false;
    }
}
'@
