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

using System.Windows;

namespace Winsight.Samples.SimpleCounter;

/// <summary>
/// Modal dialog used by E2E scenarios to exercise window-scoped locators:
/// it is a second top-level window of the same process.
/// </summary>
public partial class NoteDialog : Window
{
    public NoteDialog()
    {
        InitializeComponent();
    }

    public string NoteText => NoteTextBox.Text;

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
