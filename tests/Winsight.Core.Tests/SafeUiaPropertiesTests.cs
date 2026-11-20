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

using System.Drawing;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.EventHandlers;
using FlaUI.Core.Exceptions;
using FlaUI.Core.Identifiers;
using FlaUI.Core.Patterns;
using FlaUI.Core.Patterns.Infrastructure;
using FlaUI.UIA3;
using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.UiAutomation;
using Xunit;

namespace Winsight.Core.Tests;

public sealed class SafeUiaPropertiesTests
{
    private sealed class ThrowingPropertyAutomationElement : FrameworkAutomationElementBase
    {
        private readonly HashSet<int> _throwingPropertyIds;
        private readonly Dictionary<int, object?> _values;

        public ThrowingPropertyAutomationElement(
            AutomationBase automation,
            HashSet<int> throwingPropertyIds,
            Dictionary<int, object?> values)
            : base(automation)
        {
            _throwingPropertyIds = throwingPropertyIds;
            _values = values;
        }

        public override void SetFocus()
        {
        }

        protected override object InternalGetPropertyValue(int propertyId, bool cached, bool useDefaultIfNotSupported)
        {
            if (_throwingPropertyIds.Contains(propertyId))
            {
                throw new PropertyNotSupportedException(PropertyId.Find(Automation.AutomationType, propertyId));
            }
            if (_values.TryGetValue(propertyId, out object? value))
            {
                return value!;
            }
            return Automation.NotSupportedValue;
        }

        protected override object InternalGetPattern(int patternId, bool cached)
        {
            if (patternId == Automation.PatternLibrary.ExpandCollapsePattern.Id)
            {
                return new ThrowingExpandCollapsePattern(this);
            }
            throw new PatternNotSupportedException(PatternId.Find(Automation.AutomationType, patternId));
        }

        public override AutomationElement[] FindAll(TreeScope treeScope, ConditionBase condition) => [];

        public override AutomationElement? FindFirst(TreeScope treeScope, ConditionBase condition) => null;

        public override AutomationElement[] FindAllWithOptions(
            TreeScope treeScope,
            ConditionBase condition,
            TreeTraversalOptions traversalOptions,
            AutomationElement root) => [];

        public override AutomationElement? FindFirstWithOptions(
            TreeScope treeScope,
            ConditionBase condition,
            TreeTraversalOptions traversalOptions,
            AutomationElement root) => null;

        public override AutomationElement? FindAt(TreeScope treeScope, int index, ConditionBase condition) => null;

        public override bool TryGetClickablePoint(out Point point)
        {
            point = Point.Empty;
            return false;
        }

        public override ActiveTextPositionChangedEventHandlerBase RegisterActiveTextPositionChangedEvent(
            TreeScope treeScope,
            Action<AutomationElement, ITextRange> action) => throw new System.NotSupportedException();

        public override AutomationEventHandlerBase RegisterAutomationEvent(
            EventId @event,
            TreeScope treeScope,
            Action<AutomationElement, EventId> action) => throw new System.NotSupportedException();

        public override PropertyChangedEventHandlerBase RegisterPropertyChangedEvent(
            TreeScope treeScope,
            Action<AutomationElement, PropertyId, object> action,
            PropertyId[] properties) => throw new System.NotSupportedException();

        public override StructureChangedEventHandlerBase RegisterStructureChangedEvent(
            TreeScope treeScope,
            Action<AutomationElement, StructureChangeType, int[]> action) => throw new System.NotSupportedException();

        public override NotificationEventHandlerBase RegisterNotificationEvent(
            TreeScope treeScope,
            Action<AutomationElement, NotificationKind, NotificationProcessing, string, string> action) =>
            throw new System.NotSupportedException();

        public override TextEditTextChangedEventHandlerBase RegisterTextEditTextChangedEventHandler(
            TreeScope treeScope,
            TextEditChangeType textEditChangeType,
            Action<AutomationElement, TextEditChangeType, string[]> action) => throw new System.NotSupportedException();

        public override PatternId[] GetSupportedPatterns() => [];

        public override PropertyId[] GetSupportedProperties() => [];

        public override AutomationElement? GetUpdatedCache() => null;

        public override AutomationElement[] GetCachedChildren() => [];

        public override AutomationElement GetCachedParent() => null!;

        public override int GetHashCode() => 0;

        public override object GetCurrentMetadataValue(PropertyId targetId, int metadataId) => Automation.NotSupportedValue;

        public override void UnregisterActiveTextPositionChangedEventHandler(ActiveTextPositionChangedEventHandlerBase eventHandler) { }
        public override void UnregisterAutomationEventHandler(AutomationEventHandlerBase eventHandler) { }
        public override void UnregisterPropertyChangedEventHandler(PropertyChangedEventHandlerBase eventHandler) { }
        public override void UnregisterStructureChangedEventHandler(StructureChangedEventHandlerBase eventHandler) { }
        public override void UnregisterNotificationEventHandler(NotificationEventHandlerBase eventHandler) { }
        public override void UnregisterTextEditTextChangedEventHandler(TextEditTextChangedEventHandlerBase eventHandler) { }

        private UnsupportedAutomationPattern<T> Unsupported<T>() where T : IPattern => new(Automation.PatternLibrary.InvokePattern);

        protected override IAutomationPattern<IAnnotationPattern> InitializeAnnotationPattern() => Unsupported<IAnnotationPattern>();
        protected override IAutomationPattern<IDockPattern> InitializeDockPattern() => Unsupported<IDockPattern>();
        protected override IAutomationPattern<IDragPattern> InitializeDragPattern() => Unsupported<IDragPattern>();
        protected override IAutomationPattern<IDropTargetPattern> InitializeDropTargetPattern() => Unsupported<IDropTargetPattern>();
        protected override IAutomationPattern<IExpandCollapsePattern> InitializeExpandCollapsePattern() =>
            new SupportedAutomationPattern<IExpandCollapsePattern>(new ThrowingExpandCollapsePattern(this));
        protected override IAutomationPattern<IGridItemPattern> InitializeGridItemPattern() => Unsupported<IGridItemPattern>();
        protected override IAutomationPattern<IGridPattern> InitializeGridPattern() => Unsupported<IGridPattern>();
        protected override IAutomationPattern<IInvokePattern> InitializeInvokePattern() => Unsupported<IInvokePattern>();
        protected override IAutomationPattern<IItemContainerPattern> InitializeItemContainerPattern() => Unsupported<IItemContainerPattern>();
        protected override IAutomationPattern<ILegacyIAccessiblePattern> InitializeLegacyIAccessiblePattern() => Unsupported<ILegacyIAccessiblePattern>();
        protected override IAutomationPattern<IMultipleViewPattern> InitializeMultipleViewPattern() => Unsupported<IMultipleViewPattern>();
        protected override IAutomationPattern<IObjectModelPattern> InitializeObjectModelPattern() => Unsupported<IObjectModelPattern>();
        protected override IAutomationPattern<IRangeValuePattern> InitializeRangeValuePattern() => Unsupported<IRangeValuePattern>();
        protected override IAutomationPattern<IScrollItemPattern> InitializeScrollItemPattern() => Unsupported<IScrollItemPattern>();
        protected override IAutomationPattern<IScrollPattern> InitializeScrollPattern() => Unsupported<IScrollPattern>();
        protected override IAutomationPattern<ISelection2Pattern> InitializeSelection2Pattern() => Unsupported<ISelection2Pattern>();
        protected override IAutomationPattern<ISelectionItemPattern> InitializeSelectionItemPattern() => Unsupported<ISelectionItemPattern>();
        protected override IAutomationPattern<ISelectionPattern> InitializeSelectionPattern() => Unsupported<ISelectionPattern>();
        protected override IAutomationPattern<ISpreadsheetItemPattern> InitializeSpreadsheetItemPattern() => Unsupported<ISpreadsheetItemPattern>();
        protected override IAutomationPattern<ISpreadsheetPattern> InitializeSpreadsheetPattern() => Unsupported<ISpreadsheetPattern>();
        protected override IAutomationPattern<IStylesPattern> InitializeStylesPattern() => Unsupported<IStylesPattern>();
        protected override IAutomationPattern<ISynchronizedInputPattern> InitializeSynchronizedInputPattern() => Unsupported<ISynchronizedInputPattern>();
        protected override IAutomationPattern<ITableItemPattern> InitializeTableItemPattern() => Unsupported<ITableItemPattern>();
        protected override IAutomationPattern<ITablePattern> InitializeTablePattern() => Unsupported<ITablePattern>();
        protected override IAutomationPattern<IText2Pattern> InitializeText2Pattern() => Unsupported<IText2Pattern>();
        protected override IAutomationPattern<ITextChildPattern> InitializeTextChildPattern() => Unsupported<ITextChildPattern>();
        protected override IAutomationPattern<ITextEditPattern> InitializeTextEditPattern() => Unsupported<ITextEditPattern>();
        protected override IAutomationPattern<ITextPattern> InitializeTextPattern() => Unsupported<ITextPattern>();
        protected override IAutomationPattern<ITogglePattern> InitializeTogglePattern() => Unsupported<ITogglePattern>();
        protected override IAutomationPattern<ITransform2Pattern> InitializeTransform2Pattern() => Unsupported<ITransform2Pattern>();
        protected override IAutomationPattern<ITransformPattern> InitializeTransformPattern() => Unsupported<ITransformPattern>();
        protected override IAutomationPattern<IValuePattern> InitializeValuePattern() => Unsupported<IValuePattern>();
        protected override IAutomationPattern<IVirtualizedItemPattern> InitializeVirtualizedItemPattern() => Unsupported<IVirtualizedItemPattern>();
        protected override IAutomationPattern<IWindowPattern> InitializeWindowPattern() => Unsupported<IWindowPattern>();

#nullable disable
        private sealed class UnsupportedAutomationPattern<T>(PatternId patternId) : IAutomationPattern<T> where T : IPattern
        {
            public bool IsSupported => false;
            public T Pattern => throw new PatternNotSupportedException(patternId);
            public T PatternOrDefault => default;
            public bool TryGetPattern(out T pattern)
            {
                pattern = default;
                return false;
            }
        }

        private sealed class SupportedAutomationPattern<T>(T instance) : IAutomationPattern<T> where T : IPattern
        {
            public bool IsSupported => true;
            public T Pattern => instance;
            public T PatternOrDefault => instance;
            public bool TryGetPattern(out T pattern)
            {
                pattern = instance;
                return true;
            }
        }
#nullable restore

        private sealed class ThrowingExpandCollapsePattern : ExpandCollapsePatternBase<object>
        {
            public ThrowingExpandCollapsePattern(FrameworkAutomationElementBase frameworkAutomationElement)
                : base(frameworkAutomationElement, new object())
            {
            }

            public override void Collapse()
            {
            }

            public override void Expand()
            {
            }
        }
    }

    private static AutomationElement CreateThrowingElement(UIA3Automation automation)
    {
        HashSet<int> throwing =
        [
            automation.PropertyLibrary.Element.IsOffscreen.Id,
            automation.PropertyLibrary.Element.BoundingRectangle.Id,
            automation.PropertyLibrary.ExpandCollapse.ExpandCollapseState.Id,
        ];
        Dictionary<int, object?> values = new()
        {
            [automation.PropertyLibrary.Element.ControlType.Id] = 50000, // UIA_ButtonControlTypeId
            [automation.PropertyLibrary.Element.Name.Id] = "File",
            [automation.PropertyLibrary.Element.AutomationId.Id] = "FileMenu",
            [automation.PropertyLibrary.Element.ClassName.Id] = "Microsoft.UI.Xaml.Controls.MenuBarItem",
            [automation.PropertyLibrary.Element.IsEnabled.Id] = true,
            [automation.PropertyLibrary.Element.RuntimeId.Id] = new[] { 42, 1001 },
        };
        return new AutomationElement(new ThrowingPropertyAutomationElement(automation, throwing, values));
    }

    [Fact]
    public void UiaSceneDriver_Read_handles_PropertyNotSupportedException_for_IsOffscreen_BoundingRectangle_ExpandCollapseState()
    {
        using UIA3Automation automation = new();
        AutomationElement element = CreateThrowingElement(automation);
        WindowIdentity window = new(1, 0x100, new WindowBounds(0, 0, 800, 600));

        SceneElement node = UiaSceneDriver.Read(element, window, "e1", parentIdentity: "root");

        node.Name.Should().Be("File");
        node.Role.Should().Be("Button");
        node.Enabled.Should().BeTrue();
        node.Visible.Should().BeTrue("unsupported IsOffscreen defaults to false (visible)");
        node.Bounds.Should().Be(new WindowBounds(0, 0, 0, 0));
        node.ExpandCollapseState.Should().BeNull();
        node.ParentIdentity.Should().Be("root");
    }

    [Fact]
    public void UiTreeInspector_BuildSnapshotWithChildren_handles_unsupported_properties()
    {
        using UIA3Automation automation = new();
        AutomationElement element = CreateThrowingElement(automation);

        UiElementNode node = UiTreeInspector.BuildSnapshotWithChildren(element, []);

        node.Name.Should().Be("File");
        node.AutomationId.Should().Be("FileMenu");
        node.ControlType.Should().Be("Button");
        node.IsEnabled.Should().BeTrue();
        node.IsVisible.Should().BeTrue();
        node.Width.Should().Be(0);
        node.Height.Should().Be(0);
    }

    [Fact]
    public void ElementStateReader_BuildState_handles_unsupported_properties()
    {
        using UIA3Automation automation = new();
        AutomationElement element = CreateThrowingElement(automation);
        ElementMatch match = new(
            element,
            new ElementCandidate(0, "FileMenu", "File", "Button", "MenuBarItem", "Notepad"),
            new IntPtr(0x100));

        ElementState state = ElementStateReader.BuildState(match);

        state.Name.Should().Be("File");
        state.IsEnabled.Should().BeTrue();
        state.IsVisible.Should().BeTrue();
        state.X.Should().Be(0);
        state.Y.Should().Be(0);
        state.Width.Should().Be(0);
        state.Height.Should().Be(0);
    }
}
