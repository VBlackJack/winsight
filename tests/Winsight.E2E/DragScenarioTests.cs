// Copyright 2026 Julien Bombled
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.E2E;

/// <summary>
/// drag against WPF OLE drag and drop (DragDrop.DoDragDrop): reorder a
/// TabItem inside its TabControl, and drop a tab on a TreeView item.
/// </summary>
[Trait("Category", "E2E")]
public class DragScenarioTests
{
    private const string TabOrderId = "TabOrder";
    private const string DropStatusId = "DropStatus";
    private const string TabItemType = "TabItem";
    private const string TreeItemType = "TreeItem";

    [Fact]
    public void Dragging_a_tab_onto_another_tab_reorders_the_tab_strip()
    {
        using WpfLabSession lab = WpfLabSession.Start();

        OperationResult dragged = new ActionInvoker().Drag(
            lab.ProcessId,
            DragEndpoint.ForElement(new ElementLocator(Name: "gamma", ControlType: TabItemType)),
            DragEndpoint.ForElement(new ElementLocator(Name: "alpha", ControlType: TabItemType)));

        dragged.Success.Should().BeTrue(dragged.Error);
        lab.WaitForName(TabOrderId, "Tabs: gamma,alpha,beta").Should().BeTrue(
            $"the tab strip should be reordered: order=\"{lab.ReadName(TabOrderId)}\", drop=\"{lab.ReadName(DropStatusId)}\"");
    }

    [Fact]
    public void Dropping_a_tab_on_a_tree_item_reaches_the_tree_drop_handler()
    {
        using WpfLabSession lab = WpfLabSession.Start();

        OperationResult dragged = new ActionInvoker().Drag(
            lab.ProcessId,
            DragEndpoint.ForElement(new ElementLocator(Name: "beta", ControlType: TabItemType)),
            DragEndpoint.ForElement(new ElementLocator(NameContains: "lab-c,", ControlType: TreeItemType)));

        dragged.Success.Should().BeTrue(dragged.Error);
        lab.WaitForName(DropStatusId, "Dropped beta on lab-c").Should().BeTrue(
            $"the tree item should receive the drop: drop=\"{lab.ReadName(DropStatusId)}\"");
    }

    [Fact]
    public void A_drag_endpoint_must_be_an_element_or_a_point()
    {
        OperationResult result = new ActionInvoker().Drag(
            Environment.ProcessId,
            new DragEndpoint(),
            DragEndpoint.ForPoint(10, 10));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("source");
    }
}
