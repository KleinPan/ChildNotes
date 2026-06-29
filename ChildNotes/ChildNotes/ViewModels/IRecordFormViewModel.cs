namespace ChildNotes.ViewModels;

/// <summary>
/// 所有记录表单 VM 的统一契约。
/// 仅统一 Validate 调度；BuildDto 因返回不同 DTO 类型仍由各表单强类型保留，
/// 宿主 VM 的 Save switch 仍调用强类型 BuildDto。
/// </summary>
public interface IRecordFormViewModel
{
    /// <summary>校验表单，失败时返回错误信息。</summary>
    bool Validate(out string error);
}
