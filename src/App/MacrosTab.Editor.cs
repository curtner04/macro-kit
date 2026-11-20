using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using MouseButton = MacroKit.Core.MouseButton;
using System.Windows.Threading;
using MacroKit.Core;
using MacroKit.Macros;


namespace MacroKit.App
{
    /// <summary>The Macros tab, part 3: the steps in the Lines, Tiles and Code views, and the summary line under them.</summary>
    public sealed partial class MacrosTab
    {
        // ------------------------------------------------------------------ steps preview

        /// <summary>The script box and the live list of steps next to it (problems show in red).</summary>
        FrameworkElement BuildScriptAndSteps()
        {
            var host = new Grid();
            host.Children.Add(steps);
            host.Children.Add(stepsEmpty);
            problemsBox = new Border { Padding = new Thickness(4), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = host, MaxHeight = 150, Margin = new Thickness(0, 4, 0, 0) };
            Ui.Ref(problemsBox, Border.BackgroundProperty, "Input");
            Ui.Ref(problemsBox, Border.BorderBrushProperty, "Border");
            stepsHeader.Margin = new Thickness(0, 6, 0, 0);
            stepsSummary.Margin = new Thickness(0, 3, 0, 0);
            AutomationProperties.SetName(script, "Super Macro text");
            AutomationProperties.SetName(steps, "Problems in the Super Macro text");
            return Ui.Stack(0, script, stepsHeader, problemsBox);   // the step count and time per pass are on the line under the view
        }

        void ShowSteps()
        {
            stepsLoading = true;
            steps.Items.Clear();
            if (current == null) { stepsLoading = false; return; }
            parsed = MacroParser.Parse(current.Script, current.NewLineIsEnter);
            foreach (ParseError error in parsed.Errors)
            {
                var row = Ui.Text("Line " + error.Line + ", column " + error.Column + ":  " + error.Message, "Bad", false, 12.5, true);
                steps.Items.Add(new ListBoxItem { Content = row, Tag = error, Padding = new Thickness(8, 4, 8, 4) });
            }
            // The steps themselves are the editor above; here only what needs fixing is listed (click one to find it in the text).
            int number = 1, missing = 0;
            foreach (MacroStep step in parsed.Steps)
            {
                if (IsMissingMacro(step))
                {
                    var row = Ui.Text("Step " + number.ToString(CultureInfo.InvariantCulture) + ":  " + step.Describe() + "  (you have no macro by this name)", "Bad", false, 12.5, true);
                    steps.Items.Add(new ListBoxItem { Content = row, Tag = step, Padding = new Thickness(8, 3, 8, 3) });
                    missing++;
                }
                number++;
            }
            int problems = parsed.Errors.Count + missing;
            stepsEmpty.Visibility = Visibility.Collapsed;
            if (problemsBox != null) problemsBox.Visibility = problems > 0 ? Visibility.Visible : Visibility.Collapsed;
            stepsHeader.Text = (parsed.Steps.Count == 1 ? "1 step" : parsed.Steps.Count + " steps")
                + (problems > 0 ? "   ·   " + problems + (problems == 1 ? " problem" : " problems") : parsed.Steps.Count > 0 ? ", no problems" : "");
            Ui.Ref(stepsHeader, TextBlock.ForegroundProperty, problems > 0 ? "Bad" : "Muted");
            stepsHeader.Visibility = problems > 0 ? Visibility.Visible : Visibility.Collapsed;   // otherwise the summary line says it
            stepsSummary.Text = parsed.Steps.Count == 0 ? "" : "About " + AppController.FormatDuration(parsed.EstimatedMs(current)) + " per pass" + RepeatTotal();
            stepsLoading = false;
        }

        string RepeatTotal()
        {
            if (current.Repeat != RepeatMode.Times || current.RepeatTimes < 2) return "";
            long total = parsed.EstimatedMs(current) * current.RepeatTimes + (long)current.RepeatDelayMs * (current.RepeatTimes - 1);
            return ", about " + AppController.FormatDuration(total) + " in all";
        }

        void SelectStepInScript()
        {
            if (stepsLoading) return;
            var item = steps.SelectedItem as ListBoxItem;
            if (item == null) return;
            int index = -1, length = 0;
            var step = item.Tag as MacroStep;
            var error = item.Tag as ParseError;
            if (step != null) { index = step.Index; length = step.Length; }
            else if (error != null) { index = error.Index; length = error.Length; }
            if (index < 0 || index > script.Text.Length) return;
            script.Select(index, Math.Min(length, script.Text.Length - index));
            script.ScrollToLine(script.GetLineIndexFromCharacterIndex(index));
        }

        // ------------------------------------------------------------------ plain editors

        string EditorKind { get { return editor != null && editor == tileEditor ? EditorKinds.Tiles : EditorKinds.Text; } }

        /// <summary>Shows the typed editor or the key tiles editor; both edit the same plain text.</summary>
        void UseEditor(string kind)
        {
            if (EditorKinds.Normal(kind) == EditorKinds.Tiles)
            {
                if (tileEditor == null) { tileEditor = new KeyTileEditor(() => owner); tileEditor.Changed += PlainEdited; }
                editor = tileEditor;
            }
            else
            {
                if (textEditor == null) { textEditor = new PlainTextEditor(() => owner); textEditor.Changed += PlainEdited; }
                editor = textEditor;
            }
            if (current != null) ShowPlain(PlainMacro.PlainFor(current));
            else ShowPlain("");
            ApplyLayout();
        }

        /// <summary>Lines or Tiles: the steps in words or as key tiles (and out of the Code view).</summary>
        void ChooseEditor(string kind)
        {
            bool fromCode = c.Settings.ShowStreamDeckCode;
            if (EditorKinds.Normal(kind) == EditorKind && !fromCode) return;
            c.Settings.ShowStreamDeckCode = false;
            c.Settings.MacroEditor = EditorKinds.Normal(kind);
            c.MarkDirty(SaveParts.Settings);
            if (EditorKinds.Normal(c.Settings.MacroEditor) != EditorKind) UseEditor(c.Settings.MacroEditor);
            else ApplyLayout();
        }

        /// <summary>Code: the same steps as Super Macro text, the form a Stream Deck Super Macro action takes.</summary>
        void ChooseCode()
        {
            if (c.Settings.ShowStreamDeckCode) return;
            c.Settings.ShowStreamDeckCode = true;
            c.MarkDirty(SaveParts.Settings);
            ApplyLayout();
        }

        /// <summary>The Code view is on: the palette adds to the Super Macro text, which is then the only editor showing.</summary>
        bool CodeView { get { return c.Settings.ShowStreamDeckCode; } }

        /// <summary>Shows the chosen view (Lines, Tiles or Code), the full command list and the open option card, as saved in Settings.</summary>
        void ApplyLayout()
        {
            if (codePanel == null || palette == null || optionCards == null || editor == null) return;
            bool code = CodeView;
            UIElement view = code ? codePanel : editor.Root;
            if (!ReferenceEquals(editorHost.Child, view)) editorHost.Child = view;
            palette.Visibility = c.Settings.ShowAllCommands ? Visibility.Visible : Visibility.Collapsed;
            allCommands.Content = c.Settings.ShowAllCommands ? "Hide all commands" : "Show all commands";
            ShowOptionCard();
            bool tiles = EditorKind == EditorKinds.Tiles;
            MarkChosen(viewLines, !code && !tiles);
            MarkChosen(viewTiles, !code && tiles);
            MarkChosen(viewCode, code);
            if (code) ShowSteps();
        }

        /// <summary>A chosen chip is tinted and outlined, and says so to a screen reader.</summary>
        static void MarkChosen(Button chip, bool chosen)
        {
            chip.SetResourceReference(Control.BackgroundProperty, chosen ? "Selected" : "Card2");
            chip.SetResourceReference(Control.BorderBrushProperty, chosen ? "Accent" : "Border");
            AutomationProperties.SetItemStatus(chip, chosen ? "chosen" : "");
        }

        /// <summary>Puts plain text into the editor without counting it as an edit.</summary>
        void ShowPlain(string text)
        {
            if (editor == null) return;
            syncing = true;
            editor.Text = text ?? "";
            syncing = false;
            PlainResult result = PlainMacro.ToScript(text ?? "");
            editor.Show(result);
            ShowPlainSummary(result);
        }

        /// <summary>The user changed the steps: the macro's Super Macro text follows.</summary>
        void PlainEdited()
        {
            if (syncing || loading || current == null) return;
            string text = editor.Text;
            PlainResult result = PlainMacro.ToScript(text);
            current.Plain = text;
            current.Script = result.Script;
            c.MarkDirty(SaveParts.Macros);
            syncing = true;
            script.Text = result.Script;
            syncing = false;
            codeDirty = false;
            editor.Show(result);
            ShowPlainSummary(result);
            UpdateExampleNote();
            parseTimer.Stop();
            parseTimer.Start();
        }

        /// <summary>The Stream Deck code was changed: the steps follow.</summary>
        void PlainFromCode()
        {
            if (current == null) return;
            ShowPlain(PlainMacro.ToPlain(current.Script, current.NewLineIsEnter));
        }

        /// <summary>True for a "run another macro" step that names a macro that does not exist (a name with a $variable can't be checked).</summary>
        bool IsMissingMacro(MacroStep step)
        {
            return step.Kind == StepKind.RunMacro && (step.Value ?? "").IndexOf('$') < 0
                && !c.Macros.Any(m => string.Equals((m.Name ?? "").Trim(), (step.Value ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The steps were translated again: remember them and show the summary line.</summary>
        void ShowPlainSummary(PlainResult result)
        {
            lastPlain = result;
            summaryParse = null;
            ShowSummary();
        }

        /// <summary>The line under the steps: how many, how long, what needs fixing. Uses the last translation, so it can follow the options cheaply.</summary>
        void ShowSummary()
        {
            PlainResult result = lastPlain;
            if (result == null) { plainSummary.Text = ""; return; }
            var problems = result.Lines.Where(l => l.Error != null).ToList();
            int count = result.Lines.Count(l => l.Kind != PlainKind.Empty && l.Kind != PlainKind.Note);
            if (problems.Count > 0)
            {
                plainSummary.Text = (problems.Count == 1 ? "1 step needs" : problems.Count + " steps need") + " fixing before the macro can run.  " + FirstProblem(result);
                Ui.Ref(plainSummary, TextBlock.ForegroundProperty, "Bad");
                return;
            }
            Ui.Ref(plainSummary, TextBlock.ForegroundProperty, "Muted");
            if (count == 0 || current == null) { plainSummary.Text = ""; return; }
            if (summaryParse == null) summaryParse = MacroParser.Parse(current.Script, current.NewLineIsEnter);
            long pass = summaryParse.EstimatedMs(current);
            string text = (count == 1 ? "1 step" : count + " steps") + "  ·  about " + AppController.FormatDuration(pass) + " per pass";
            if (current.Repeat == RepeatMode.Times && current.RepeatTimes > 1)
                text += ", about " + AppController.FormatDuration(pass * current.RepeatTimes + (long)current.RepeatDelayMs * (current.RepeatTimes - 1)) + " in all";
            // Not an error that stops the run: the macro it names may be made later. But it is worth saying before Run does nothing.
            List<string> missing = summaryParse.Steps.Where(IsMissingMacro).Select(x => (x.Value ?? "").Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (missing.Count > 0)
            {
                text += "  ·  " + (missing.Count == 1 ? "no macro called " : "no macros called ") + string.Join(", ", missing.Select(n => "“" + n + "”"));
                Ui.Ref(plainSummary, TextBlock.ForegroundProperty, "Bad");
            }
            plainSummary.Text = text;
        }

        /// <summary>For an example that is still as shipped: what it does and what to do first.</summary>
        void UpdateExampleNote()
        {
            string note = current != null && Examples.IsUnchanged(current) ? Examples.Describe(current.Name) : "";
            exampleNote.Text = note;
            exampleNote.Visibility = note.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        void CopyCode()
        {
            if (current == null || string.IsNullOrEmpty(current.Script)) { c.SetStatus("There is no code to copy yet.", true); return; }
            try { Clipboard.SetText(current.Script); c.SetStatus("Stream Deck code copied. Paste it into a Super Macro action.", false); }
            catch (Exception ex) { c.SetStatus("Could not copy: " + ex.Message, true); }
        }

        /// <summary>The palette adds to the Super Macro text in the Code view (the only editor showing there), otherwise to the steps.</summary>
        bool CodeIsTarget { get { return CodeView; } }
    }
}
