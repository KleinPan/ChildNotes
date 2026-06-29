namespace ChildNotes.Core.Services;

public interface IReferrerCodeUtil
{
    string Encode(long userId);
    long? Decode(string code);
}
