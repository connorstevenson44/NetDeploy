namespace NetDeploy.Core.Text;

/// <summary>
/// One connected change between two texts: the old text's [OldStart, OldEnd) became the new text's [NewStart, NewEnd).
/// One side is empty for text that was only added or only taken out.
/// </summary>
public sealed record TextChange(int OldStart, int OldLength, int NewStart, int NewLength)
{
    public int OldEnd => OldStart + OldLength;
    public int NewEnd => NewStart + NewLength;
}
