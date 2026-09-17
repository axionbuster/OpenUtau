using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia.Headless.XUnit;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.App {
    [Collection(RenderSingletonCollection.Name)]
    public class PartBoundaryTest : IDisposable {
        readonly DocManager doc = DocManager.Inst;
        readonly UProject previous;
        readonly object? previousThread;
        readonly FieldInfo threadField = typeof(DocManager).GetField("mainThread", BindingFlags.NonPublic | BindingFlags.Instance)!;
        readonly UProject project = Core.Format.Ustx.Create();
        readonly UVoicePart part = new UVoicePart { Duration = 1920 };
        public PartBoundaryTest() {
            previousThread = threadField.GetValue(doc);
            threadField.SetValue(doc, Thread.CurrentThread);
            ThreadGuard.SetUiThread(Thread.CurrentThread);
            previous = doc.TakeProjectForTest(project);
            project.parts.Add(part);
            project.ValidateFull();
        }
        public void Dispose() {
            doc.ExecuteCmd(new LoadProjectNotification(previous));
            threadField.SetValue(doc, previousThread);
        }
        [AvaloniaTheory]
        [InlineData(false)]
        [InlineData(true)]
        public void GrowthCommitsWithNoteAndUndoRedo(bool deferred) {
            var note = project.CreateNote(60, 1800, 240);
            doc.StartUndoGroup(deferValidate: deferred);
            doc.ExecuteCmd(new AddNoteCommand(part, note));
            Assert.Equal(1920, part.Duration);
            doc.EndUndoGroup();
            Assert.Equal(2400, part.Duration);
            for (int i = 0; i < 3; i++) {
                doc.Undo();
                Assert.Empty(part.notes);
                Assert.Equal(1920, part.Duration);
                doc.Redo();
                Assert.Single(part.notes);
                Assert.Equal(2400, part.Duration);
            }
            doc.StartUndoGroup();
            doc.ExecuteCmd(new MoveNoteCommand(part, note, -1200, 0));
            doc.EndUndoGroup();
            Assert.Equal(2400, part.Duration);
            doc.StartUndoGroup();
            doc.ExecuteCmd(new RemoveNoteCommand(part, note));
            doc.EndUndoGroup();
            Assert.Equal(2400, part.Duration);
        }
        [AvaloniaFact]
        public void DragOutAndBackDoesNotGrowAndRollbackDoesNotGrow() {
            var note = project.CreateNote(60, 0, 480);
            part.notes.Add(note);
            doc.StartUndoGroup();
            doc.ExecuteCmd(new MoveNoteCommand(part, note, 5000, 0));
            Assert.Equal(1920, part.Duration);
            doc.ExecuteCmd(new MoveNoteCommand(part, note, -5000, 0));
            doc.EndUndoGroup();
            Assert.Equal(1920, part.Duration);
            doc.StartUndoGroup();
            doc.ExecuteCmd(new ResizeNoteCommand(part, note, 5000));
            doc.RollBackUndoGroup();
            doc.EndUndoGroup();
            Assert.Equal(1920, part.Duration);
            Assert.Equal(480, note.duration);
        }
        [AvaloniaFact]
        public void UnsortedBatchUsesFarthestEndAndExactBoundaryDoesNotGrowOnReload() {
            var notes = new List<UNote> { project.CreateNote(60, 3000, 360), project.CreateNote(62, 0, 480) };
            doc.StartUndoGroup();
            doc.ExecuteCmd(new AddNoteCommand(part, notes));
            doc.EndUndoGroup();
            Assert.Equal(3360, part.Duration);
            part.AfterLoad(project, project.tracks[0]);
            Assert.Equal(3360, part.Duration);
            doc.Undo();
            Assert.Equal(1920, part.Duration);
            doc.Redo();
            Assert.Equal(3360, part.Duration);
        }
        [AvaloniaTheory]
        [InlineData("NoteDrawEditState")]
        [InlineData("NoteMoveEditState")]
        [InlineData("NoteResizeEditState")]
        public void PointerGestureCrossesBoundaryAndCommitsOnce(string stateName) {
            doc.SearchAllLegacyPlugins();
            var vm = new PianoRollViewModel();
            var editor = new Controls.PianoRoll(vm);
            var notes = vm.NotesViewModel;
            notes.OnNext(new LoadPartNotification(part, project, 0), false);
            notes.IsSnapOn = false;
            var note = project.CreateNote(60, 0, 480);
            if (stateName != "NoteDrawEditState") { part.notes.Add(note); }
            var type = typeof(Controls.PianoRoll).Assembly.GetType("OpenUtau.App.Views." + stateName)!;
            object[] args = stateName switch {
                "NoteDrawEditState" => new object[] { editor, vm, editor, false },
                "NoteMoveEditState" => new object[] { editor, vm, editor, note },
                _ => new object[] { editor, vm, editor, note, false },
            };
            var state = Activator.CreateInstance(type, args)!;
            var pointer = new Avalonia.Input.Pointer(1, Avalonia.Input.PointerType.Mouse, true);
            void Invoke(string method, int tick) => type.GetMethod(method)!.Invoke(state,
                new object[] { pointer, notes.TickToneToCenterPoint(tick, 60) });
            Invoke("Begin", 0);
            Invoke("Update", 2400);
            Assert.True(part.notes.Max(n => n.End) > 1920);
            Assert.Equal(1920, part.Duration);
            Invoke("End", 2400);
            int committedDuration = part.Duration;
            Assert.True(committedDuration > 1920);
            doc.Undo();
            Assert.Equal(1920, part.Duration);
            doc.Redo();
            Assert.Equal(committedDuration, part.Duration);
        }
        [AvaloniaFact]
        public void BrowsingBeyondPartNeverChangesDocument() {
            var vm = new NotesViewModel();
            vm.OnNext(new LoadPartNotification(part, project, 0), false);
            vm.Bounds = new Avalonia.Rect(0, 0, 900, 500);
            Assert.True(vm.TickCount > part.Duration);
            vm.TickOffset = vm.HScrollBarMax;
            Assert.True(vm.HScrollBarMax > vm.TickOffset);
            Assert.Equal(1920, part.Duration);
        }
    }
}
