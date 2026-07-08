using ChildNotes.ViewModels;

namespace ChildNotes.Tests;

/// <summary>
/// 验证补给表单（补充剂/用药）的默认项、自定义项持久化、切换类型、选中同步等逻辑。
/// 注意：测试环境中用户未登录（UserId 为空），自定义项仅内存操作，不写DB。
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
    /// 默认 CurrentAllItems 应包含补充剂默认项（5项）
    /// </summary>
    [Fact]
    public void DefaultCurrentAllItems_ShouldContainSupplementDefaults()
    {
        var vm = new SupplementFormViewModel();
        Assert.Equal(5, vm.CurrentAllItems.Count);
        Assert.All(vm.CurrentAllItems.Take(5), item => Assert.False(item.IsCustom));
    }

    /// <summary>
    /// 切换到用药后，CurrentAllItems 应为药品默认项（5项）
    /// </summary>
    [Fact]
    public void SwitchToMedicine_CurrentAllItems_ShouldContainMedicineDefaults()
    {
        var vm = new SupplementFormViewModel();
        vm.SwitchType("medicine");
        Assert.Equal(5, vm.CurrentAllItems.Count);
        Assert.All(vm.CurrentAllItems, item => Assert.False(item.IsCustom));
    }

    /// <summary>
    /// 切换类型时，旧类型的选中状态应被清空
    /// </summary>
    [Fact]
    public void SwitchType_ShouldClearOldSelection()
    {
        var vm = new SupplementFormViewModel();
        vm.SupplementCommonItems[0].IsSelected = true;
        vm.SupplementCommonItems[2].IsSelected = true;
        vm.SwitchType("medicine");
        Assert.All(vm.SupplementCommonItems, item => Assert.False(item.IsSelected));
        Assert.All(vm.MedicineCommonItems, item => Assert.False(item.IsSelected));
    }

    /// <summary>
    /// 切换类型时，名称字段应被清空
    /// </summary>
    [Fact]
    public void SwitchType_ShouldClearName()
    {
        var vm = new SupplementFormViewModel();
        vm.SupplementCommonItems[0].IsSelected = true;
        Assert.Equal("维生素D", vm.Name);
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
    /// BuildDto 中的 Type 字段应反映当前类型
    /// </summary>
    [Fact]
    public void BuildDto_Type_ShouldReflectCurrentType()
    {
        var vm = new SupplementFormViewModel();
        vm.SupplementCommonItems[0].IsSelected = true;
        Assert.Equal("supplement", vm.BuildDto().Type);

        vm.SwitchType("medicine");
        vm.MedicineCommonItems[0].IsSelected = true;
        Assert.Equal("medicine", vm.BuildDto().Type);
    }

    /// <summary>
    /// 补充剂默认项应为 5 项（对齐小程序 DEFAULT_SUPPLEMENTS）
    /// </summary>
    [Fact]
    public void SupplementCommonItems_ShouldHave5Defaults()
    {
        var vm = new SupplementFormViewModel();
        var expected = new[] { "维生素D", "益生菌", "DHA", "钙剂", "铁剂" };
        Assert.Equal(expected, vm.SupplementCommonItems.Select(x => x.Name).ToArray());
    }

    /// <summary>
    /// 药品默认项应为 5 项（对齐小程序 DEFAULT_MEDICINES）
    /// </summary>
    [Fact]
    public void MedicineCommonItems_ShouldHave5Defaults()
    {
        var vm = new SupplementFormViewModel();
        var expected = new[] { "泰诺林", "布洛芬", "美林", "蒙脱石散", "口服补液盐" };
        Assert.Equal(expected, vm.MedicineCommonItems.Select(x => x.Name).ToArray());
    }

    /// <summary>
    /// 多次来回切换不应影响默认列表完整性
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
        Assert.Equal(5, vm.SupplementCommonItems.Count);
        Assert.Equal(5, vm.MedicineCommonItems.Count);
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

    /// <summary>
    /// 勾选默认项应立即同步到 Name 字段
    /// </summary>
    [Fact]
    public void ToggleDefaultItem_IsSelected_ShouldSyncToName()
    {
        var vm = new SupplementFormViewModel();
        vm.SupplementCommonItems[0].IsSelected = true;
        Assert.Equal("维生素D", vm.Name);

        vm.SupplementCommonItems[2].IsSelected = true;
        Assert.Equal("维生素D、DHA", vm.Name);

        // 取消勾选应同步移除
        vm.SupplementCommonItems[0].IsSelected = false;
        Assert.Equal("DHA", vm.Name);
    }

    /// <summary>
    /// 添加自定义项后应自动选中，并出现在 CurrentAllItems 中（IsCustom=true）
    /// </summary>
    [Fact]
    public void AddCustomItem_ShouldAutoSelectAndAppearInCurrentAllItems()
    {
        var vm = new SupplementFormViewModel();
        vm.CustomItem = "鱼肝油";
        vm.AddCustomItem();

        // 应出现在自定义集合中
        Assert.Contains(vm.CustomSupplementItems, x => x.Name == "鱼肝油" && x.IsCustom);
        // 应出现在 CurrentAllItems 中（默认5 + 自定义1 = 6）
        Assert.Equal(6, vm.CurrentAllItems.Count);
        Assert.Contains(vm.CurrentAllItems, x => x.Name == "鱼肝油" && x.IsCustom);
        // 应自动选中
        Assert.Equal("鱼肝油", vm.Name);
        // 输入框应清空
        Assert.Equal(string.Empty, vm.CustomItem);
    }

    /// <summary>
    /// 勾选默认项 + 添加自定义项应合并到 Name，且 Validate 通过
    /// </summary>
    [Fact]
    public void SelectDefaultAndAddCustom_Name_ShouldMergeAndValidate()
    {
        var vm = new SupplementFormViewModel();
        vm.SupplementCommonItems[0].IsSelected = true;   // 维生素D
        vm.CustomItem = "鱼肝油";
        vm.AddCustomItem();

        Assert.Equal("维生素D、鱼肝油", vm.Name);
        Assert.True(vm.Validate(out var error));
        Assert.Equal(string.Empty, error);

        var dto = vm.BuildDto();
        Assert.Equal("维生素D、鱼肝油", dto.Name);
    }

    /// <summary>
    /// 仅勾选默认项（不点自定义添加）也应 Validate 通过
    /// </summary>
    [Fact]
    public void OnlySelectDefaultItems_Validate_ShouldPass()
    {
        var vm = new SupplementFormViewModel();
        vm.SupplementCommonItems[1].IsSelected = true;
        vm.SupplementCommonItems[3].IsSelected = true;
        Assert.True(vm.Validate(out _));
        Assert.Equal("益生菌、钙剂", vm.Name);
    }

    /// <summary>
    /// 添加重复名称（与默认项同名）应失败并提示错误
    /// </summary>
    [Fact]
    public void AddCustomItem_DuplicateWithDefault_ShouldFail()
    {
        var vm = new SupplementFormViewModel();
        vm.CustomItem = "维生素D";  // 与默认项同名
        vm.AddCustomItem();

        Assert.Equal("该名称已存在", vm.ErrorMessage);
        Assert.Empty(vm.CustomSupplementItems);
        Assert.Equal(5, vm.CurrentAllItems.Count);  // 未增加
    }

    /// <summary>
    /// 添加空名称应失败并提示错误
    /// </summary>
    [Fact]
    public void AddCustomItem_EmptyName_ShouldFail()
    {
        var vm = new SupplementFormViewModel();
        vm.CustomItem = "   ";
        vm.AddCustomItem();

        Assert.Equal("请输入名称", vm.ErrorMessage);
        Assert.Empty(vm.CustomSupplementItems);
    }

    /// <summary>
    /// 切换类型后再切回，自定义项应保留（测试环境中未写DB，但内存集合保留）
    /// </summary>
    [Fact]
    public void SwitchType_BackAndForth_CustomItems_ShouldRemain()
    {
        var vm = new SupplementFormViewModel();
        vm.CustomItem = "鱼肝油";
        vm.AddCustomItem();
        Assert.Single(vm.CustomSupplementItems);

        vm.SwitchType("medicine");
        Assert.Empty(vm.CustomMedicineItems);  // 用药类型无自定义项

        vm.SwitchType("supplement");
        Assert.Single(vm.CustomSupplementItems);  // 补充剂的自定义项仍在
        Assert.Equal(6, vm.CurrentAllItems.Count);  // 5默认 + 1自定义
    }

    /// <summary>
    /// 自定义项的 IsCustom 标志应为 true，默认项应为 false
    /// </summary>
    [Fact]
    public void IsCustom_Flag_ShouldDifferentiateItems()
    {
        var vm = new SupplementFormViewModel();
        // 默认项 IsCustom=false
        Assert.All(vm.SupplementCommonItems, x => Assert.False(x.IsCustom));
        Assert.All(vm.MedicineCommonItems, x => Assert.False(x.IsCustom));

        vm.CustomItem = "鱼肝油";
        vm.AddCustomItem();
        // 自定义项 IsCustom=true
        Assert.All(vm.CustomSupplementItems, x => Assert.True(x.IsCustom));
    }
}
