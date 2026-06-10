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

using System.Windows.Input;
using Winsight.Samples.SimpleCounter.Commands;

namespace Winsight.Samples.SimpleCounter.ViewModels;

public sealed class CounterViewModel : ViewModelBase
{
    // Fixture data exercised by the select_item E2E scenario.
    private static readonly string[] AvailableOptions = { "Alpha", "Beta", "Gamma" };

    // Private backing fields - these are what Winsight reads through the read_managed_state tool.
    private int _internalCount;
    private string _selectedOption = AvailableOptions[0];
    private bool _featureEnabled;
    private string _lastNote = string.Empty;

    public int Count
    {
        get => _internalCount;
        private set => SetField(ref _internalCount, value);
    }

    public IReadOnlyList<string> Options => AvailableOptions;

    public string SelectedOption
    {
        get => _selectedOption;
        set => SetField(ref _selectedOption, value);
    }

    public bool IsFeatureEnabled
    {
        get => _featureEnabled;
        set => SetField(ref _featureEnabled, value);
    }

    public string LastNote
    {
        get => _lastNote;
        set => SetField(ref _lastNote, value);
    }

    public ICommand IncrementCommand { get; }

    public ICommand ResetCommand { get; }

    public CounterViewModel()
    {
        IncrementCommand = new RelayCommand(_ => Count++);
        ResetCommand = new RelayCommand(_ => Count = 0);
    }
}
