using DataSpace.Core;
using Xunit;

namespace DataSpace.Tests;

public sealed class FormTabOrderTests
{
    private static FormDefinition Form() => new()
    {
        Name = "Entry", Source = "Table1", Controls = [
            new() { Id = "first", Caption = "First", Field = "Title", X = 200, Y = 200 },
            new() { Id = "label", Caption = "Caption", Kind = LayoutControlKind.Label, X = 0, Y = 0 },
            new() { Id = "second", Caption = "Second", Field = "Title", X = 200, Y = 100 },
            new() { Id = "third", Caption = "Third", Field = "Title", X = 100, Y = 100 } ]
    };
    private static DatabaseDocument Document()
    {
        var document = new DatabaseDocument(); ObjectFactory.CreateTable(document);
        document.Forms.Add(Form()); return document;
    }
    private static string[] Order(FormDefinition form, bool activeOnly = false) => FormTabOrder.Ordered(form, activeOnly).Select(c => c.Id).ToArray();

    [Fact]
    public void LegacyFormsUseCreationOrderAndAllInputsAreTabStops()
    {
        var form = Form();
        Assert.Equal(new[] { "first", "second", "third" }, Order(form));
        Assert.All(form.Controls, c => { Assert.Equal(-1, c.TabIndex); Assert.True(c.TabStop); });
        var json = DocumentCodec.Serialize(Document()).Replace(",\"TabIndex\":-1", "").Replace(",\"TabStop\":true", "");
        Assert.Equal(Order(form), Order(DocumentCodec.Deserialize(json).Forms[0]));
    }
    [Fact]
    public void AutoOrderUsesTopThenLeftWithoutChangingDrawOrderOrGeometry()
    {
        var form = Form(); var drawOrder = form.Controls.ToArray(); var draft = new FormTabOrderDraft(form);
        draft.AutoOrder(); Assert.Equal(new[] { "third", "second", "first" }, draft.Entries.Select(e => e.Id));
        Assert.True(draft.Apply(form)); Assert.Equal(new[] { "third", "second", "first" }, Order(form));
        Assert.Equal(drawOrder, form.Controls); Assert.Equal(200, form.Controls[0].Y); Assert.Equal(-1, form.Controls[1].TabIndex);
    }
    [Fact]
    public void DraftDoesNotMutateSourceUntilAppliedAndSkippedInputsKeepAnIndex()
    {
        var form = Form(); var draft = new FormTabOrderDraft(form);
        draft.MoveTo("first", 2); draft.SetTabStop("second", false);
        Assert.Equal(new[] { "first", "second", "third" }, Order(form));
        Assert.True(form.Controls[2].TabStop);
        draft.Apply(form);
        Assert.Equal(new[] { "second", "third", "first" }, Order(form));
        Assert.Equal(new[] { "third", "first" }, Order(form, true));
        Assert.Equal(0, form.Controls[2].TabIndex);
    }
    [Fact]
    public void EqualAndUnsetIndicesHaveStableCreationOrderTies()
    {
        var form = Form(); form.Controls[0].TabIndex = 2; form.Controls[3].TabIndex = 2;
        Assert.Equal(new[] { "first", "second", "third" }, Order(form));
    }
    [Fact]
    public void EmptyAndAllSkippedFormsAreValid()
    {
        var empty = new FormDefinition(); var draft = new FormTabOrderDraft(empty); draft.AutoOrder(); Assert.False(draft.Apply(empty));
        var form = Form(); foreach (var control in form.Controls) control.TabStop = false;
        Assert.Empty(FormTabOrder.Ordered(form, true)); Assert.Equal(3, FormTabOrder.Ordered(form).Count);
    }
    [Theory]
    [InlineData(-2)] [InlineData(32768)]
    public void InvalidIndicesAreRejectedAtDocumentLoadAndWorkspaceBoundary(int index)
    {
        var document = Document(); document.Forms[0].Controls[0].TabIndex = index;
        Assert.Throws<DataSpaceException>(() => DocumentCodec.Deserialize(DocumentCodec.Serialize(document)));
        var workspace = new DatabaseWorkspace(Document()); var before = workspace.Document;
        Assert.Throws<DataSpaceException>(() => workspace.Edit("invalid", d => d.Forms[0].Controls[0].TabIndex = index));
        Assert.Same(before, workspace.Document);
    }
    [Fact]
    public void AmbiguousControlIdentitiesAreRejected()
    {
        var form = Form(); form.Controls[2].Id = form.Controls[0].Id;
        Assert.Throws<DataSpaceException>(() => new FormTabOrderDraft(form));
        form.Controls[2] = null!; Assert.Throws<DataSpaceException>(() => FormTabOrder.Ordered(form));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void StaleDraftCannotOverwriteChangedControls(int change)
    {
        var form = Form(); var draft = new FormTabOrderDraft(form); draft.MoveTo("first", 2);
        switch (change)
        {
            case 0: form.Controls[0].TabStop = false; break;
            case 1: form.Controls[0].X++; break;
            case 2: form.Controls[0].Kind = LayoutControlKind.Label; break;
            case 3: form.Controls.Reverse(); break;
        }
        var indices = form.Controls.Select(c => c.TabIndex).ToArray();
        Assert.Throws<DataSpaceException>(() => draft.Apply(form)); Assert.Equal(indices, form.Controls.Select(c => c.TabIndex));
    }
    [Fact]
    public void InvalidMoveAndUnknownIdentityKeepDraftIntact()
    {
        var draft = new FormTabOrderDraft(Form()); var before = draft.Entries.ToArray();
        Assert.Throws<DataSpaceException>(() => draft.MoveTo("first", 3));
        Assert.Throws<DataSpaceException>(() => draft.MoveTo("missing", 0));
        Assert.Throws<DataSpaceException>(() => draft.SetTabStop("label", false));
        Assert.Equal(before, draft.Entries);
    }
    [Fact]
    public void CopyPersistenceUndoAndTargetedRecordSnapshotsRetainTabSettings()
    {
        var workspace = new DatabaseWorkspace(Document());
        workspace.Edit("tab order", d => { var plan = new FormTabOrderDraft(d.Forms[0]); plan.MoveTo("third", 0); plan.SetTabStop("first", false); plan.Apply(d.Forms[0]); });
        var expected = new[] { "third", "first", "second" };
        var copy = DocumentSnapshot.Copy(workspace.Document); Assert.Equal(expected, Order(copy.Forms[0]));
        copy.Forms[0].Controls[0].TabStop = true; Assert.False(workspace.Document.Forms[0].Controls[0].TabStop);
        Assert.Equal(expected, Order(DocumentCodec.Deserialize(DocumentCodec.Serialize(workspace.Document)).Forms[0]));
        workspace.Undo(); Assert.Equal(new[] { "first", "second", "third" }, Order(workspace.Document.Forms[0]));
        workspace.Redo(); Assert.Equal(expected, Order(workspace.Document.Forms[0]));
        workspace.AppendRecords("row", "Table1", ["Title"], new[] { new string?[] { "Value" } });
        Assert.Equal(expected, Order(workspace.Document.Forms[0])); Assert.False(workspace.Document.Forms[0].Controls[0].TabStop);
    }
    [Fact]
    public void RandomMovesProduceContiguousIndicesAndPreserveTheDrawingList()
    {
        var random = new Random(2874);
        for (var test = 0; test < 30; test++)
        {
            var form = Form(); var plan = new FormTabOrderDraft(form); var expected = Order(form).ToList(); var drawOrder = form.Controls.ToArray();
            for (var step = 0; step < 20; step++)
            {
                var from = random.Next(expected.Count); var to = random.Next(expected.Count); var id = expected[from];
                expected.RemoveAt(from); expected.Insert(to, id); plan.MoveTo(id, to);
            }
            plan.Apply(form); Assert.Equal(expected, Order(form)); Assert.Equal(drawOrder, form.Controls);
            Assert.Equal(new[] { 0, 1, 2 }, FormTabOrder.Ordered(form).Select(c => c.TabIndex));
        }
    }
}
