namespace ChildNotes.Core.Services;

public interface IReferrerCodeUtil
{
    string Encode(string userId);
    string? Decode(string code);
}
