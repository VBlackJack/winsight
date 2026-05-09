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
    // Private backing field - this is what Winsight reads through the read_managed_state tool.
    private int _internalCount;

    public int Count
    {
        get => _internalCount;
        private set => SetField(ref _internalCount, value);
    }

    public ICommand IncrementCommand { get; }

    public ICommand ResetCommand { get; }

    public CounterViewModel()
    {
        IncrementCommand = new RelayCommand(_ => Count++);
        ResetCommand = new RelayCommand(_ => Count = 0);
    }
}
