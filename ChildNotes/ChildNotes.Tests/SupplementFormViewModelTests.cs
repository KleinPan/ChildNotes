using ChildNotes.ViewModels;

namespace ChildNotes.Tests;

/// <summary>
/// 验证补给表单（补充剂/用药）切换标签页时常用内容动态更新逻辑。
/// 覆盖场景：默认类型、切换后清空选中、切换后清空名称、CurrentCommonItems 通知、BuildDto 类型字段。
/// </summary>
public class SupplementFormViewModelTests
{
    /// <summary>
    /// 默认类型应为 supplement（补充剂），对齐小程序 formType 默认值
    /// </summary>
    [Fact]
    public void DefaultType_ShouldBeSupplement()
    {
        var vm = new SupplementFormViewModel();
        Assert.Equal("supplement", vm.SuppType);
    }

    /// <summary>
    /// 默认常用项应为补充剂列表
    /// </summary>
    [Fact]
    public void DefaultCurrentCommonItems_ShouldBeSupplementList()
    {
        var vm = new SupplementFormViewModel();
        Assert.Same(vm.SupplementCommonItems, vm.CurrentCommonItems);
    }

    /// <summary>
    /// 切换到用药后，CurrentCommonItems 应为药品列表
    /// </summary>
    [Fact]
    public void SwitchToMedicine_CurrentCommonItems_ShouldBeMedicineList()
    {
        var vm = new SupplementFormViewModel();
        vm.SwitchType("medicine");
        Assert.Same(vm.MedicineCommonItems, vm.CurrentCommonItems);
    }

    /// <summary>
    /// 切换到补充剂后，CurrentCommonItems 应为补充剂列表
    /// </summary>
    [Fact]
    public void SwitchToSupplement_CurrentCommonItems_ShouldBeSupplementList()
    {
        var vm = new SupplementFormViewModel();
        vm.SwitchType("medicine"); // 先切到用药
        vm.SwitchType("supplement"); // 再切回补充剂
        Assert.Same(vm.SupplementCommonItems, vm.CurrentCommonItems);
    }

    /// <summary>
    /// 切换类型时，旧类型的选中状态应被清空
    /// </summary>
    [Fact]
    public void SwitchType_ShouldClearOldSelection()
    {
        var vm = new SupplementFormViewModel();
        // 选中补充剂列表中的项
        vm.SupplementCommonItems[0].IsSelected = true;
        vm.SupplementCommonItems[2].IsSelected = true;
        // 切换到用药
        vm.SwitchType("medicine");
        // 补充剂列表的选中状态应全部清空
        Assert.All(vm.SupplementCommonItems, item => Assert.False(item.IsSelected));
        Assert.All(vm.MedicineCommonItems, item => Assert.False(item.IsSelected));
    }

    /// <summary>
    /// 切换类型时，名称字段应被清空（对齐小程序 switchType 中 name: '' 逻辑）
    /// </summary>
    [Fact]
    public void SwitchType_ShouldClearName()
    {
        var vm = new SupplementFormViewModel();
        vm.Name = "维生素D、益生菌";
        vm.SwitchType("medicine");
        Assert.Equal(string.Empty, vm.Name);
    }

    /// <summary>
    /// 切换类型时，自定义输入字段应被清空
    /// </summary>
    [Fact]
    public void SwitchType_ShouldClearCustomItem()
    {
        var vm = new SupplementFormViewModel();
        vm.CustomItem = "自定义内容";
        vm.SwitchType("medicine");
        Assert.Equal(string.Empty, vm.CustomItem);
    }

    /// <summary>
    /// 切换类型后选中新列表的项，RefreshNameFromSelection 应只反映新列表的选中项
    /// </summary>
    [Fact]
    public void SwitchType_ThenSelectNewItems_RefreshName_ShouldReflectNewList()
    {
        var vm = new SupplementFormViewModel();
        // 切换到用药并选中两项
        vm.SwitchType("medicine");
        vm.MedicineCommonItems[0].IsSelected = true;
        vm.MedicineCommonItems[3].IsSelected = true;
        vm.RefreshNameFromSelection();
        Assert.Contains("泰诺林", vm.Name);
        Assert.Contains("蒙脱石散", vm.Name);
    }

    /// <summary>
    /// BuildDto 中的 Type 字段应反映当前类型
    /// </summary>
    [Fact]
    public void BuildDto_Type_ShouldReflectCurrentType()
    {
        var vm = new SupplementFormViewModel();
        vm.Name = "维生素D";
        Assert.Equal("supplement", vm.BuildDto().Type);

        vm.SwitchType("medicine");
        vm.Name = "泰诺林";
        Assert.Equal("medicine", vm.BuildDto().Type);
    }

    /// <summary>
    /// 补充剂常用项列表应包含 6 项且内容对齐小程序 DEFAULT_SUPPLEMENTS
    /// </summary>
    [Fact]
    public void SupplementCommonItems_ShouldMatchMiniProgram()
    {
        var vm = new SupplementFormViewModel();
        var expected = new[] { "维生素D", "益生菌", "DHA", "钙剂", "铁剂", "锌剂" };
        Assert.Equal(expected, vm.SupplementCommonItems.Select(x => x.Name).ToArray());
    }

    /// <summary>
    /// 药品常用项列表应包含 6 项且内容对齐小程序 DEFAULT_MEDICINES
    /// </summary>
    [Fact]
    public void MedicineCommonItems_ShouldMatchMiniProgram()
    {
        var vm = new SupplementFormViewModel();
        var expected = new[] { "泰诺林", "布洛芬", "美林", "蒙脱石散", "口服补液盐", "西替利嗪" };
        Assert.Equal(expected, vm.MedicineCommonItems.Select(x => x.Name).ToArray());
    }

    /// <summary>
    /// 多次来回切换不应影响列表完整性
    /// </summary>
    [Fact]
    public void MultipleSwitches_Lists_ShouldRemainIntact()
    {
        var vm = new SupplementFormViewModel();
        for (int i = 0; i < 5; i++)
        {
            vm.SwitchType("medicine");
            vm.SwitchType("supplement");
        }
        Assert.Equal(6, vm.SupplementCommonItems.Count);
        Assert.Equal(6, vm.MedicineCommonItems.Count);
        Assert.Same(vm.SupplementCommonItems, vm.CurrentCommonItems);
    }

    /// <summary>
    /// 补充剂列表与药品列表内容应完全不同
    /// </summary>
    [Fact]
    public void SupplementAndMedicineLists_ShouldHaveDifferentContent()
    {
        var vm = new SupplementFormViewModel();
        var suppNames = vm.SupplementCommonItems.Select(x => x.Name).ToHashSet();
        var medNames = vm.MedicineCommonItems.Select(x => x.Name).ToHashSet();
        Assert.NotEqual(suppNames, medNames);
    }
}
