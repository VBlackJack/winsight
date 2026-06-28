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

using Winsight.Core.Models;

namespace Winsight.Mcp.Tools;

internal sealed record PlannedAgentAction(string Action, bool RequiresReview, string? ReviewReason);

internal static class AgentStepPlanner
{
    private const string SelectionReviewReason =
        "Selection requires an itemName. This draft clicks the candidate to open or focus it; " +
        "observe the options, then use select_item or a run_steps select step with itemName.";

    public static PlannedAgentAction ChooseAction(string goal, string? text, UiInteractable? element)
    {
        if (!string.IsNullOrEmpty(text) ||
            goal.Contains("type", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("enter", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("fill", StringComparison.OrdinalIgnoreCase) ||
            element?.ControlType == "Edit")
        {
            return new PlannedAgentAction("type", RequiresReview: false, ReviewReason: null);
        }

        bool selectionIntent =
            goal.Contains("select", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("choose", StringComparison.OrdinalIgnoreCase);
        bool selectionControl = element?.ControlType is "ComboBox" or "List" or "ListItem" or "TabItem";
        if (selectionIntent || selectionControl)
        {
            return new PlannedAgentAction("click", RequiresReview: true, SelectionReviewReason);
        }

        return new PlannedAgentAction("click", RequiresReview: false, ReviewReason: null);
    }
}
