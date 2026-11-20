// Copyright 2026 Julien Bombled
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Winsight.Core.Models;
using Winsight.Core.Modules.Clipboard;
using FlaUIControlType = FlaUI.Core.Definitions.ControlType;

namespace Winsight.Core.Modules.UiAutomation;

/// <summary>
/// Executes user-level actions (click, type, select) on elements located
/// through the strict <see cref="ElementFinder"/>. Elements that are
/// off-screen are scrolled into view before any physical mouse action.
/// </summary>
public sealed partial class ActionInvoker
{
    private static readonly object DesktopInputGate = new();
    private const int DefaultTooltipTimeoutMs = 1500;
    private const int MaxTooltipTimeoutMs = 10_000;
    private const int TooltipPollIntervalMs = 50;
    private const int TooltipProximityPx = 400;
    private const int TooltipTextDepth = 3;
    private const int PreHoverSettleMs = PointerDispatch.DefaultSettleMs;
    private const int FocusInputSettleMs = 100;
    private const int SystemMetricDragWidth = 68;
    private const int SystemMetricDragHeight = 69;
    public const string TypeKeysModeKeys = "keys";
    public const string TypeKeysModePaste = "paste";
    public const int DefaultRestoreDelayMs = 300;
    public const int MaxRestoreDelayMs = 5000;
    private const string RestoredFull = "full";
    private const string RestoredPartial = "partial";
    private const string RestoredFailed = "failed";
    private const string FocusedWindowChanged =
        "The target window changed while it was brought to the foreground. Observe again before retrying.";

    /// <summary>
    /// Click the located element. <see cref="ClickStyle.Primary"/> prefers the
    /// Invoke pattern, then Toggle, then a physical left click; DoubleClick
    /// and RightClick are always physical mouse actions at the element's location.
    /// </summary>
    public OperationResult Click(
        int processId,
        ElementLocator locator,
        ClickStyle style = ClickStyle.Primary,
        string? windowTitle = null)
    {
        if (CheckInputIntegrity(processId, out IntegrityComparison integrity) is { } blocked)
        {
            return blocked;
        }
        using DesktopInputLease desktopLease = DesktopInputLease.Acquire();
        lock (DesktopInputGate)
        {
            using UIA3Automation automation = new();
            ElementSearchResult search = ElementFinder.FindSingle(automation, processId, locator, windowTitle);
            if (!search.Found)
            {
                return OperationResult.FailureWith(search.Error!, search.Candidates);
            }

            AutomationElement element = search.Element!;
            ScrollIntoViewIfOffscreen(element);

            switch (style)
            {
                case ClickStyle.DoubleClick:
                case ClickStyle.RightClick:
                    return PointerDispatch.Click(ClickablePoint(element), style, out InputDispatchInfo? dispatch, integrity: integrity)
                        ?? OperationResult.OkWith(new { dispatch });

                default:
                    return InvokePrimaryAction(element, integrity);
            }
        }
    }

    /// <summary>
    /// Set the located element's value via the Value pattern, falling back
    /// to focusing the element and typing through the keyboard. An element
    /// inside a WebView2 surface is refused unless
    /// <paramref name="allowWebView2Input"/> is set (see <see cref="WebView2FocusClassifier"/>).
    /// </summary>
    public OperationResult Type(
        int processId,
        ElementLocator locator,
        string text,
        string? windowTitle = null,
        bool focusWindow = false,
        bool allowWebView2Input = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (CheckInputIntegrity(processId, out _) is { } blocked)
        {
            return blocked;
        }
        using DesktopInputLease desktopLease = DesktopInputLease.Acquire();
        lock (DesktopInputGate)
        {
            using UIA3Automation automation = new();
            ElementSearchResult search = ElementFinder.FindSingle(automation, processId, locator, windowTitle);
            if (!search.Found)
            {
                return OperationResult.FailureWith(search.Error!, search.Candidates);
            }

            AutomationElement element = search.Element!;
            if (!allowWebView2Input &&
                FocusedElementReader.WebView2EvidenceFor(automation, element) is { Count: > 0 } evidence)
            {
                return OperationResult.Failure(WebView2InputRefusal("The located element", evidence));
            }
            ScrollIntoViewIfOffscreen(element);
            if (focusWindow)
            {
                WindowFocusResult focus = WindowFocus.FocusWindow(
                    search.Match!.WindowHandle,
                    search.Match.Candidate.WindowTitle,
                    processId);
                if (!focus.Success)
                {
                    return OperationResult.Failure(focus.Error!);
                }
            }

            FlaUI.Core.Patterns.IValuePattern? valuePattern = element.Patterns.Value.PatternOrDefault;
            if (valuePattern is not null && !valuePattern.IsReadOnly.ValueOrDefault)
            {
                valuePattern.SetValue(text);
                return OperationResult.Ok();
            }

            if (ForegroundInputPolicy.CheckForegroundForKeyboardInput("type_text") is { } fgRejected)
            {
                return fgRejected;
            }
            if (PrepareInputContext(out InputDispatchInfo dispatch) is { } contextBlocked)
            {
                return contextBlocked;
            }
            element.Focus();
            Keyboard.Type(text);
            return OperationResult.OkWith(new { dispatch });
        }
    }

    /// <summary>
    /// Focus a process window, verify it became the foreground window, and
    /// dispatch a keyboard chord such as "Ctrl+Shift+N". The chord is refused
    /// when keyboard focus then sits inside a WebView2 surface, unless
    /// <paramref name="allowWebView2Input"/> is set. The result reports the
    /// element that received the chord.
    /// </summary>
    public OperationResult SendKeyChord(
        int processId,
        string chord,
        string? windowTitle = null,
        bool allowWebView2Input = false)
    {
        if (CheckInputIntegrity(processId, out IntegrityComparison integrity) is { } blocked)
        {
            return blocked;
        }
        using DesktopInputLease desktopLease = DesktopInputLease.Acquire();
        lock (DesktopInputGate)
        {
            if (!KeyboardChordParser.TryParse(chord, out VirtualKeyShort[]? keys, out string? parseError))
            {
                return OperationResult.Failure(parseError!);
            }

            WindowFocusResult focus = WindowFocus.FocusProcessWindow(processId, windowTitle);
            if (!focus.Success)
            {
                return OperationResult.Failure(focus.Error!);
            }

            Thread.Sleep(FocusInputSettleMs);
            FocusedElementInfo? focused;
            using (UIA3Automation automation = new())
            {
                focused = FocusedElementReader.ReadFocus(automation, processId);
            }
            if (!allowWebView2Input && focused is { IsWebView2: true })
            {
                return OperationResult.FailureWith(
                    WebView2InputRefusal("Keyboard focus", focused.WebView2Evidence),
                    focused);
            }
            if (GetForegroundWindow() != focus.WindowHandle)
            {
                return OperationResult.Failure(
                    $"{TextTyper.ForegroundChangedCode}: the target window lost the foreground before the chord \"{chord}\" was sent.");
            }
            if (ForegroundInputPolicy.CheckForegroundForKeyboardInput("send_key_chord") is { } fgRejected)
            {
                return fgRejected;
            }
            if (PrepareInputContext(out InputDispatchInfo dispatch) is { } contextBlocked)
            {
                return contextBlocked;
            }
            try
            {
                DispatchKeyboardChord(keys);
            }
            catch (Win32Exception ex)
            {
                return SendInputFailure(ex, integrity);
            }
            return OperationResult.OkWith(new KeyChordResult(
                chord,
                focus.WindowTitle,
                focus.WindowHandle.ToInt64(),
                focused,
                dispatch));
        }
    }

    /// <summary>
    /// Type text into the focused control of the target's foreground window,
    /// without a UI Automation element: the surface of a GPU-drawn UI (iced,
    /// winit, egui) has none. The window is raised and verified first, and input
    /// inside WebView2 is refused unless <paramref name="allowWebView2Input"/>,
    /// as for <see cref="SendKeyChord"/>. <c>keys</c> mode sends each character
    /// as a Unicode keystroke and stops if the window loses the foreground.
    /// <c>paste</c> mode saves the clipboard, puts the text on it (kept out of
    /// history and the cloud), sends Ctrl+V, waits
    /// <paramref name="restoreDelayMs"/> for the application to read it, and
    /// puts the saved clipboard back. Callers check <c>ClipboardPolicy</c> before
    /// paste mode.
    /// </summary>
    public OperationResult TypeKeys(
        int processId,
        string text,
        string? windowTitle = null,
        bool paste = false,
        bool allowWebView2Input = false,
        int? restoreDelayMs = null)
    {
        if (TextTyper.Validate(text) is { } invalid)
        {
            return OperationResult.Failure(invalid);
        }
        if (restoreDelayMs is < 0 or > MaxRestoreDelayMs)
        {
            return OperationResult.Failure(string.Create(
                CultureInfo.InvariantCulture,
                $"restoreDelayMs must be between 0 and {MaxRestoreDelayMs} (got {restoreDelayMs})."));
        }

        if (CheckInputIntegrity(processId, out IntegrityComparison integrity) is { } blocked)
        {
            return blocked;
        }

        using DesktopInputLease desktopLease = DesktopInputLease.Acquire();
        lock (DesktopInputGate)
        {
            WindowFocusResult focus = WindowFocus.FocusProcessWindow(processId, windowTitle);
            if (!focus.Success)
            {
                return OperationResult.Failure(focus.Error!);
            }
            Thread.Sleep(FocusInputSettleMs);
            FocusedElementInfo? focused;
            using (UIA3Automation automation = new())
            {
                focused = FocusedElementReader.ReadFocus(automation, processId);
            }
            if (!allowWebView2Input && focused is { IsWebView2: true })
            {
                return OperationResult.FailureWith(
                    WebView2InputRefusal("Keyboard focus", focused.WebView2Evidence),
                    focused);
            }

            if (ForegroundInputPolicy.CheckForegroundForKeyboardInput("type_keys") is { } fgRejected)
            {
                return fgRejected;
            }
            if (PrepareInputContext(out InputDispatchInfo dispatch) is { } contextBlocked)
            {
                return contextBlocked;
            }
            bool StillTargeted() => GetForegroundWindow() == focus.WindowHandle;
            TypeKeysResult sent = new(
                paste ? TypeKeysModePaste : TypeKeysModeKeys,
                text.Length,
                focus.WindowHandle.ToInt64(),
                focus.WindowTitle,
                focused,
                Dispatch: dispatch);
            if (!paste)
            {
                try
                {
                    string? stopped = TextTyper.Send(TextTyper.Plan(text), StillTargeted, TextTyper.SendStroke);
                    return stopped is null ? OperationResult.OkWith(sent) : OperationResult.FailureWith(stopped, sent);
                }
                catch (Win32Exception ex)
                {
                    return SendInputFailure(ex, integrity);
                }
            }
            return PasteText(text, restoreDelayMs ?? DefaultRestoreDelayMs, StillTargeted, sent, integrity);
        }
    }

    private static OperationResult PasteText(
        string text,
        int restoreDelayMs,
        Func<bool> stillTargeted,
        TypeKeysResult sent,
        IntegrityComparison integrity)
    {
        ClipboardService clipboard = new();
        ClipboardSnapshot saved;
        try
        {
            saved = clipboard.Capture();
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            return OperationResult.FailureWith(ex.Message, sent);
        }

        OperationResult outcome;
        OperationResult written = clipboard.WriteText(text);
        if (!written.Success)
        {
            outcome = OperationResult.FailureWith(written.Error!, sent);
        }
        else if (!stillTargeted())
        {
            outcome = OperationResult.FailureWith(
                $"{TextTyper.ForegroundChangedCode}: the target window lost the foreground before the paste; " +
                "nothing was pasted.",
                sent);
        }
        else
        {
            try
            {
                DispatchKeyboardChord([VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V]);
                // The application reads the clipboard when it handles the chord, not when it is sent.
                Thread.Sleep(restoreDelayMs);
                outcome = OperationResult.OkWith(sent);
            }
            catch (Win32Exception ex)
            {
                outcome = SendInputFailure(ex, integrity);
            }
        }

        string restored;
        try
        {
            clipboard.Restore(saved);
            restored = saved.Skipped.Count == 0 ? RestoredFull : RestoredPartial;
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            restored = RestoredFailed;
        }
        TypeKeysResult reported = sent with { Restored = restored, SkippedFormats = saved.Skipped.Count };
        return outcome.Success
            ? OperationResult.OkWith(reported)
            : OperationResult.FailureWith(outcome.Error!, reported);
    }


    internal static OperationResult? PrepareInputContext(out InputDispatchInfo dispatch)
    {
        InputContextSnapshot context = InputContext.Capture();
        if (InputContext.EvaluateBlock(context) is { } blocked)
        {
            dispatch = InputContext.ToDispatchInfo(context, null);
            return OperationResult.FailureWith(
                blocked.Message,
                new
                {
                    code = blocked.Code,
                    restrictions = blocked.Restrictions,
                    inputContext = blocked.Context,
                });
        }
        DesktopSwitchAttempt? desktopSwitch = null;
        if (!context.DesktopMatchesInput)
        {
            desktopSwitch = InputContext.TryAttachToInputDesktop(context);
            if (desktopSwitch.Succeeded)
            {
                context = InputContext.Capture();
            }
        }
        dispatch = InputContext.ToDispatchInfo(context, desktopSwitch);
        return null;
    }

    private static OperationResult SendInputFailure(Win32Exception ex, IntegrityComparison integrity) =>
        OperationResult.FailureWith(
            ex.Message,
            new
            {
                code = "send_input_failed",
                win32Error = ex.NativeErrorCode,
                currentIntegrity = integrity.Current.Name,
                currentRid = integrity.Current.Rid,
                targetIntegrity = integrity.Target.Name,
                targetRid = integrity.Target.Rid,
                uipiBlocked = integrity.IsUipiBlocked,
            });

    internal static OperationResult? CheckInputIntegrity(int processId, out IntegrityComparison comparison)
    {
        comparison = ProcessIntegrity.CheckTarget(processId);
        if (!comparison.IsUipiBlocked)
        {
            return null;
        }

        return OperationResult.FailureWith(
            comparison.Warning!,
            new
            {
                code = ProcessIntegrity.UipiBlockedCode,
                currentIntegrity = comparison.Current.Name,
                currentRid = comparison.Current.Rid,
                targetIntegrity = comparison.Target.Name,
                targetRid = comparison.Target.Rid,
                targetAccessDenied = comparison.Target.AccessDenied,
                targetWin32Error = comparison.Target.Win32Error,
            });
    }

    private static OperationResult InvokePrimaryAction(AutomationElement element, IntegrityComparison integrity)
    {
        FlaUI.Core.Patterns.IInvokePattern? invokePattern = element.Patterns.Invoke.PatternOrDefault;
        if (invokePattern is not null)
        {
            invokePattern.Invoke();
            return OperationResult.Ok();
        }

        FlaUI.Core.Patterns.ITogglePattern? togglePattern = element.Patterns.Toggle.PatternOrDefault;
        if (togglePattern is not null)
        {
            togglePattern.Toggle();
            return OperationResult.Ok();
        }

        return PointerDispatch.Click(ClickablePoint(element), ClickStyle.Primary, out InputDispatchInfo? dispatch, integrity: integrity)
            ?? OperationResult.OkWith(new { dispatch });
    }

    internal static string WebView2InputRefusal(
        string subject,
        IReadOnlyList<string> evidence,
        bool canOverride = true) =>
        $"{subject} is inside a WebView2 surface ({string.Join(", ", evidence)}). Synthetic keyboard input " +
        "there has crashed the text input host (ctfmon), so it was not sent. Use type_into_webview (CDP) " +
        "for web content" + (canOverride ? ", or pass allowWebView2Input=true to send it anyway." : ".");

    internal static void DispatchKeyboardChord(FlaUI.Core.WindowsAPI.VirtualKeyShort[] keys) =>
        KeyChordDispatcher.Dispatch(keys);


    private static void ScrollIntoViewIfOffscreen(AutomationElement element)
    {
        try
        {
            if (element.Properties.IsOffscreen.ValueOrDefault)
            {
                element.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
            }
        }
        catch (COMException)
        {
            // Scrolling is best-effort; the action itself will surface a
            // real failure if the element stays unreachable.
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
