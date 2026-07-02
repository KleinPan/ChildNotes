using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;
using ChildNotes.Shared.Constants;

namespace ChildNotes.ViewModels;

public partial class RecordSheetViewModel : RecordFormHostViewModel
{
    /// <summary>保存成功后触发（用于刷新首页/喂养页数据）。</summary>
    public event Action? Saved;

    /// <summary>疫苗内联操作（已打/跳过/修改）后触发：刷新首页数据但不关闭抽屉，便于连续操作。</summary>
    public event Action? VaccineInlineChanged;

    /// <summary>抽屉关闭时触发（无论保存还是点 X 关闭，统一通知 Shell 重置 IsRecordSheetOpen）。</summary>
    public event Action? Closed;

    /// <summary>是否为编辑模式（true=编辑现有记录，false=新建记录）。</summary>
    [ObservableProperty] private bool _isEditMode;

    // ===== 疫苗修改流程 =====
    /// <summary>待修改的疫苗剂次（点击"修改"按钮后暂存，确认对话框用）。</summary>
    private VaccinePlanView? _pendingEditPlan;
    /// <summary>是否显示修改确认对话框。</summary>
    [ObservableProperty] private bool _showVaccineEditConfirm;
    /// <summary>确认对话框中展示的剂次名称。</summary>
    [ObservableProperty] private string _vaccineEditConfirmTitle = string.Empty;

    // ===== 疫苗取消已打流程 =====
    /// <summary>待取消的疫苗剂次（点击"取消"按钮后暂存）。</summary>
    private VaccinePlanView? _pendingCancelPlan;
    /// <summary>是否显示取消确认对话框。</summary>
    [ObservableProperty] private bool _showVaccineCancelConfirm;
    /// <summary>取消确认对话框中展示的剂次名称。</summary>
    [ObservableProperty] private string _vaccineCancelConfirmTitle = string.Empty;

    /// <summary>编辑模式下保存的记录 ID；新建模式为空字符串。</summary>
    private string _editingId = string.Empty;

    /// <summary>编辑模式下保存的原始记录日期（用于时间解析回填）。</summary>
    private DateTime _editingDate;

    public async Task OpenAsync(string type)
    {
        ActiveType = type;
        IsEditMode = false;
        _editingId = string.Empty;
        SheetTitle = BuildTitle("记录", type);
        ErrorMessage = string.Empty;
        IsVisible = true;
        // 疫苗类型需要加载时间轴：先显示抽屉占位，再异步填充数据
        if (type == RecordType.Vaccine)
        {
            await VaccineForm.LoadAsync();
        }
    }

    /// <summary>
    /// 编辑模式入口：用现有记录填充表单，标题改为「编辑xxx」。
    /// 复用与新建完全相同的表单字段，保证可编辑所有字段。
    /// </summary>
    public void Edit(ChildRecord record)
    {
        ActiveType = record.RecordType;
        IsEditMode = true;
        _editingId = record.Id;
        _editingDate = record.RecordDate;
        SheetTitle = BuildTitle("编辑", record.RecordType);
        ErrorMessage = string.Empty;
        FillForm(record);
        IsVisible = true;
    }

    /// <summary>疫苗专用：标记某剂次为「已打」并保存（原地更新 UI，不重建时间轴避免抖动）</summary>
    public async Task<bool> MarkVaccineDoneAsync(VaccinePlanView plan)
    {
        var dto = VaccineForm.MarkDone(plan);
        if (dto is null) return false;
        try
        {
            var recordId = RecordService.AddVaccine(dto);
            // 原地更新该卡片状态（只触发该卡片的 INPC 通知，不影响其他卡片）
            VaccineForm.MarkDoneInline(plan, dto.Time, recordId);
            VaccineInlineChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存失败：{ex.Message}";
            return false;
        }
    }

    /// <summary>疫苗专用：标记某剂次为「跳过」并保存（原地更新 UI）</summary>
    public async Task<bool> MarkVaccineSkippedAsync(VaccinePlanView plan)
    {
        var dto = VaccineForm.MarkSkipped(plan);
        if (dto is null) return false;
        try
        {
            var recordId = RecordService.AddVaccine(dto);
            VaccineForm.MarkSkippedInline(plan, dto.Time, recordId);
            VaccineInlineChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存失败：{ex.Message}";
            return false;
        }
    }

    /// <summary>疫苗专用：添加自定义疫苗到时间轴</summary>
    public async Task<(bool Ok, string Error)> AddCustomVaccineAsync()
    {
        var (ok, error) = await VaccineForm.AddCustomVaccineAsync();
        if (!ok) ErrorMessage = error;
        else ErrorMessage = string.Empty;
        return (ok, error);
    }

    /// <summary>请求修改已打疫苗记录：弹出确认对话框。</summary>
    public void RequestVaccineEdit(VaccinePlanView plan)
    {
        _pendingEditPlan = plan;
        VaccineEditConfirmTitle = plan.Name;
        ShowVaccineEditConfirm = true;
    }

    [RelayCommand]
    private void CancelVaccineEdit()
    {
        ShowVaccineEditConfirm = false;
        _pendingEditPlan = null;
    }

    /// <summary>确认修改：用当前选择的日期时间更新已打记录。</summary>
    [RelayCommand]
    private async Task ConfirmVaccineEditAsync()
    {
        if (_pendingEditPlan is null) return;
        var plan = _pendingEditPlan;
        var dto = VaccineForm.BuildUpdateDoneDto(plan);
        if (dto is null || plan.RecordId is null)
        {
            ShowVaccineEditConfirm = false;
            _pendingEditPlan = null;
            return;
        }
        try
        {
            RecordService.UpdateVaccine(plan.RecordId, dto);
            // 清预加载缓存，确保 LoadAsync 从 DB 重建（改时间后状态可能联动变化，不能用原地更新）
            ChildNotes.ViewModels.VaccineFormViewModel.InvalidatePreload();
            await VaccineForm.LoadAsync();
            VaccineInlineChanged?.Invoke();
            ShowVaccineEditConfirm = false;
            _pendingEditPlan = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"修改失败：{ex.Message}";
            ShowVaccineEditConfirm = false;
            _pendingEditPlan = null;
        }
    }

    /// <summary>请求取消已打疫苗记录：弹出二次确认对话框（已打记录有真实时间数据，需防误删）。</summary>
    public void RequestVaccineCancel(VaccinePlanView plan)
    {
        _pendingCancelPlan = plan;
        VaccineCancelConfirmTitle = plan.Name;
        ShowVaccineCancelConfirm = true;
    }

    /// <summary>直接取消已跳过剂次：无数据损失（跳过仅标记状态），与"跳过"操作对称，不弹窗。
    /// 删除 DB 记录并原地恢复为待接种状态。</summary>
    public void CancelVaccineSkippedDirect(VaccinePlanView plan)
    {
        if (plan.RecordId is null) return;
        try
        {
            RecordService.Delete(plan.RecordId);
            VaccineForm.CancelInline(plan);
            VaccineInlineChanged?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"取消失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void CancelVaccineCancel()
    {
        ShowVaccineCancelConfirm = false;
        _pendingCancelPlan = null;
    }

    /// <summary>确认取消：删除已打/已跳过记录，原地恢复剂次为待接种状态。
    /// 采用 CancelInline 原地更新（与 MarkDoneInline/MarkSkippedInline 对称），
    /// 避免 LoadAsync 命中预加载缓存导致 UI 仍显示旧状态。</summary>
    [RelayCommand]
    private async Task ConfirmVaccineCancelAsync()
    {
        if (_pendingCancelPlan is null) return;
        var plan = _pendingCancelPlan;
        if (plan.RecordId is null)
        {
            ShowVaccineCancelConfirm = false;
            _pendingCancelPlan = null;
            return;
        }
        try
        {
            RecordService.Delete(plan.RecordId);
            // 原地更新该卡片状态为待接种（INPC 通知触发 UI 刷新），不依赖 LoadAsync 重建
            VaccineForm.CancelInline(plan);
            VaccineInlineChanged?.Invoke();
            ShowVaccineCancelConfirm = false;
            _pendingCancelPlan = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"取消失败：{ex.Message}";
            ShowVaccineCancelConfirm = false;
            _pendingCancelPlan = null;
        }
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = string.Empty;
        if (!ValidateActiveForm()) return;
        try
        {
            if (IsEditMode)
            {
                UpdateExisting();
            }
            else
            {
                AddNew();
            }
            IsVisible = false;
            Saved?.Invoke();
            Closed?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存失败：{ex.Message}";
        }
    }

    private void AddNew()
    {
        switch (ActiveType)
        {
            case RecordType.Feed: RecordService.AddFeed(FeedForm.BuildDto()); break;
            case RecordType.Diaper: RecordService.AddDiaper(DiaperForm.BuildDto()); break;
            case RecordType.Sleep: RecordService.AddSleep(SleepForm.BuildDto()); break;
            case RecordType.Temperature: RecordService.AddTemperature(TemperatureForm.BuildDto()); break;
            case RecordType.Growth: RecordService.AddGrowth(GrowthForm.BuildDto()); break;
            case RecordType.Supplement: RecordService.AddSupplement(SupplementForm.BuildDto()); break;
            case RecordType.Pump: RecordService.AddPump(PumpForm.BuildDto()); break;
            case RecordType.Complementary: RecordService.AddComplementary(ComplementaryForm.BuildDto()); break;
            case RecordType.Abnormal: RecordService.AddAbnormal(AbnormalForm.BuildDto()); break;
            case RecordType.Activity: RecordService.AddActivity(ActivityForm.BuildDto()); break;
        }
    }

    /// <summary>编辑模式：用表单数据更新现有记录。复用 RecordFormHostViewModel.FillForm 的反向操作。</summary>
    private void UpdateExisting()
    {
        var existing = RecordService.GetById(_editingId);
        if (existing is null)
        {
            ErrorMessage = "记录不存在";
            return;
        }

        switch (ActiveType)
        {
            case RecordType.Feed:
                var feedDto = FeedForm.BuildDto();
                existing.RecordSubType = feedDto.Type;
                existing.AmountMl = feedDto.Amount;
                existing.LeftDurationSec = (feedDto.LeftDuration ?? 0) * 60;
                existing.RightDurationSec = (feedDto.RightDuration ?? 0) * 60;
                existing.DurationSec = ((feedDto.LeftDuration ?? 0) + (feedDto.RightDuration ?? 0)) * 60;
                existing.RecordTime = ParseTime(feedDto.Time, _editingDate);
                break;
            case RecordType.Diaper:
                var diaDto = DiaperForm.BuildDto();
                existing.RecordSubType = diaDto.Type;
                existing.RecordTime = ParseTime(diaDto.Time, _editingDate);
                break;
            case RecordType.Sleep:
                var slpDto = SleepForm.BuildDto();
                existing.DurationSec = (slpDto.Duration ?? 0) * 60;
                existing.RecordTime = ParseTime(slpDto.StartTime, _editingDate);
                break;
            case RecordType.Temperature:
                var tmpDto = TemperatureForm.BuildDto();
                existing.TemperatureValue = tmpDto.Temperature;
                existing.AbnormalFlag = tmpDto.Temperature >= 37.3m;
                existing.RecordTime = ParseTime(tmpDto.Time, _editingDate);
                break;
            case RecordType.Growth:
                var grwDto = GrowthForm.BuildDto();
                existing.HeightCm = grwDto.Height;
                existing.WeightKg = grwDto.Weight;
                existing.RecordTime = ParseTime(grwDto.Time, _editingDate);
                break;
            case RecordType.Supplement:
                var supDto = SupplementForm.BuildDto();
                existing.RecordSubType = supDto.Type;
                existing.RecordTime = ParseTime(supDto.Time, _editingDate);
                break;
            case RecordType.Pump:
                var pmpDto = PumpForm.BuildDto();
                existing.LeftDurationSec = (pmpDto.LeftDuration ?? 0) * 60;
                existing.RightDurationSec = (pmpDto.RightDuration ?? 0) * 60;
                existing.AmountMl = pmpDto.TotalAmount;
                existing.RecordTime = ParseTime(pmpDto.Time, _editingDate);
                break;
            case RecordType.Complementary:
                var cmpDto = ComplementaryForm.BuildDto();
                existing.RecordSubType = cmpDto.Texture;
                existing.AbnormalFlag = cmpDto.Abnormal;
                existing.RecordTime = ParseTime(cmpDto.Time, _editingDate);
                break;
            case RecordType.Abnormal:
                var abnDto = AbnormalForm.BuildDto();
                existing.TemperatureValue = abnDto.Temperature;
                existing.AbnormalFlag = true;
                // 重新计算 RecordSubType（与 AddAbnormal 保持一致）
                if (abnDto.Temperature.HasValue && abnDto.Temperature.Value >= 38m)
                    existing.RecordSubType = "fever";
                else if (abnDto.Diarrhea.Count > 0)
                    existing.RecordSubType = "diarrhea";
                else if (abnDto.Vomit)
                    existing.RecordSubType = "vomit";
                else if (abnDto.Medicine)
                    existing.RecordSubType = "medicine";
                else
                    existing.RecordSubType = null;
                existing.PayloadJson = System.Text.Json.JsonSerializer.Serialize(abnDto);
                existing.RecordTime = ParseTime(abnDto.Time, _editingDate);
                break;
            case RecordType.Activity:
                var actDto = ActivityForm.BuildDto();
                existing.RecordSubType = actDto.Category;
                existing.DurationSec = (actDto.Duration ?? 0) * 60;
                existing.RecordTime = ParseTime(actDto.Time, _editingDate);
                break;
        }
        RecordService.Update(existing);
    }

    private static DateTime ParseTime(string timeStr, DateTime date)
    {
        if (string.IsNullOrEmpty(timeStr)) return date;
        // Try full datetime format first (e.g. "yyyy-MM-dd HH:mm" from combined date+time)
        if (DateTime.TryParse(timeStr, out var dt))
            return dt;
        // Fallback to time-only format (e.g. "HH:mm")
        if (TimeSpan.TryParse(timeStr, out var ts))
            return date.Date + ts;
        return date;
    }

    /// <summary>覆写基类钩子：抽屉关闭时统一触发 Closed 事件（保存和 X 关闭共用）。</summary>
    protected override void OnSheetClosed() => Closed?.Invoke();
}
