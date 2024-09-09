using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace Loki
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        public static PlayerProfile selectedPlayerProfile = null;

        private static CharacterFile[] characterFiles;

        private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            Debug.Assert(version != null, nameof(version) + " != null");
            Title = $"{Title} v{version.Major}.{version.Minor}";

            try
            {
                characterFiles = await Task.Run(CharacterFile.LoadCharacterFiles);
                RefreshCharacterFiles((bool)ChkLoadBackupFiles.IsChecked);
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

        }

        private void RefreshCharacterFiles(bool loadBackups)
        {
            if (loadBackups)
            {
                CharacterFiles = characterFiles;
            }
            else
            {
                CharacterFiles = characterFiles.Where(f => !f.FilePath.Contains("backup", StringComparison.InvariantCultureIgnoreCase)).ToArray();                
            }
            SelectedCharacterFile = CharacterFiles.FirstOrDefault();
        }

        public static readonly DependencyProperty ProfileProperty = DependencyProperty.Register(
            "Profile", typeof(PlayerProfile), typeof(MainWindow), new PropertyMetadata(default(PlayerProfile)));

        public PlayerProfile Profile
        {
            get => (PlayerProfile) GetValue(ProfileProperty);
            set => SetValue(ProfileProperty, value);
        }

        public static readonly DependencyProperty CharacterFilesProperty = DependencyProperty.Register(
            "CharacterFiles", typeof(CharacterFile[]), typeof(MainWindow), new PropertyMetadata(default(CharacterFile[])));

        public CharacterFile[] CharacterFiles
        {
            get => (CharacterFile[]) GetValue(CharacterFilesProperty);
            set => SetValue(CharacterFilesProperty, value);
        }

        public static readonly DependencyProperty SelectedCharacterFileProperty = DependencyProperty.Register(
            "SelectedCharacterFile", typeof(CharacterFile), typeof(MainWindow), new PropertyMetadata(default(CharacterFile), SelectedCharacterChanged));

        public static readonly DependencyProperty CreateBackupProperty = DependencyProperty.Register(
            "CreateBackup", typeof(bool), typeof(MainWindow), new PropertyMetadata(true));

        public bool CreateBackup
        {
            get => (bool) GetValue(CreateBackupProperty);
            set => SetValue(CreateBackupProperty, value);
        }

        public static readonly DependencyProperty SaveInProgressProperty = DependencyProperty.Register(
            "SaveInProgress", typeof(bool), typeof(MainWindow), new PropertyMetadata(default(bool)));

        /// <summary>
        /// Gets or sets a value that indicates whether or not a Save is currently in progress.
        /// </summary>
        public bool SaveInProgress
        {
            get => (bool) GetValue(SaveInProgressProperty);
            set => SetValue(SaveInProgressProperty, value);
        }
        
        private static void SelectedCharacterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var window = (MainWindow) d;
            var characterFile = (CharacterFile) e.NewValue;
            if (characterFile != null)
            {
                window.LoadProfile(characterFile);
            }
            else
            {
                window.Profile = null;
            }
        }

        public async void LoadProfile(CharacterFile character)
        {
            try
            {
                Profile = await Task.Run(() => PlayerProfile.Read(File.OpenRead(character.FilePath)));
                character.PlayerName = Profile.PlayerName;
                selectedPlayerProfile = Profile;
            }
            catch (Exception ex)
            {
                Profile = null;
                MessageBox.Show("Error loading from character file: " + ex.Message, "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                // TODO Log
            }
        }

        public async void SaveProfile(CharacterFile character)
        {
            try
            {
                if (Profile == null) throw new InvalidOperationException("No profile loaded, cannot save");

                // Capture values of dependency properties for background thread.
                var profile = Profile;
                bool makeBackup = CreateBackup;

                SaveInProgress = true;
                Cursor = Cursors.Wait;

                await Task.Run(() =>
                {
                    
                    // Re-Tag any items with new character name if needed.
                    foreach (var slot in profile.Player.Inventory.Slots)
                    {
                        if (slot.Item != null && slot.Item.CrafterId == profile.PlayerId)
                        {
                            slot.Item.CrafterName = profile.PlayerName;
                        }
                    }
                    
                    if (makeBackup) 
                        Backup.BackupCharacter(character);

                    using var fileStream = File.Create(character.FilePath);

                    profile.Write(fileStream);
                });

                character.PlayerName = Profile.PlayerName;
                _ = ShowNotification(Loki.Properties.Resources.Character_Saved);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error saving to character file: " + ex.Message, "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                // TODO log error
            }
            finally
            {
                SaveInProgress = false;
                Cursor = null;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public CharacterFile SelectedCharacterFile
        {
            get => (CharacterFile) GetValue(SelectedCharacterFileProperty);
            set => SetValue(SelectedCharacterFileProperty, value);
        }

        private void CanSaveOrRevertExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = SelectedCharacterFile != null && !SelectedCharacterFile.Invalid && !SaveInProgress;
        }

        private void RevertExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            LoadProfile(SelectedCharacterFile);
            _ = ShowNotification(Loki.Properties.Resources.Character_Reverted);
        }

        private void SaveExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            SaveProfile(SelectedCharacterFile);
        }

        private void CanModifyAllSkillsExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Profile != null && Profile.Player.Skills.Count > 0;
        }

        private void ModifyAllSkillsExecuted(Object sender, ExecutedRoutedEventArgs e)
        {
            var percent = (float)ModifyAllSkillsSlider.Value;
            var factor = 1f + 0.01f * percent;
            var count = 0;
            foreach (var skill in Profile.Player.Skills)
            {
                if (skill.Level > 0f)
                {
                    skill.Level *= factor;
                    count++;
                }
            }

            var textToFormat = percent < 0f
                ? Loki.Properties.Resources._0__skills_decreased__1__percent
                : Loki.Properties.Resources._0__skills_increased__1__percent;
            _ = ShowNotification(string.Format(textToFormat, count, percent.ToString("+0;-#")));
        }

        private void CanRepairInventoryItemsExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Profile != null && Profile.Player.Inventory.Slots.Any(slot => slot.RepairItem.CanExecute(null));
        }

        private void RepairInventoryItemsExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            int count = 0;
            Profile?.Player.Inventory.Slots.ForEach(slot =>
            {
                if (slot.RepairItem.CanExecute(null))
                {
                    slot.RepairItem.Execute(null);
                    count++;
                }
            });
            _ = ShowNotification(string.Format(Loki.Properties.Resources.Repaired__0__items, count));
        }

        private void CanFillInventoryStacksExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Profile != null && Profile.Player.Inventory.Slots.Any(slot => slot.FillStack.CanExecute(null));
        }

        private void FillInventoryStacksExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            int count = 0;
            Profile?.Player.Inventory.Slots.ForEach(slot =>
            {
                if (slot.FillStack.CanExecute(null))
                {
                    slot.FillStack.Execute(null);
                    count++;
                }
            });

            _ = ShowNotification(string.Format(Loki.Properties.Resources.Filled__0__stacks, count));
        }

        private void ItemPickerItemMouseMove(object sender, MouseEventArgs e)
        {
            if(sender is FrameworkElement element && e.LeftButton == MouseButtonState.Pressed)
            {
                if (element.DataContext is InventoryListItem item)
                {
                    var data = new DataObject(item.ItemData);
                    DragDrop.DoDragDrop(element, data, DragDropEffects.Copy);
                }
            }
        }

        private void CanRestoreExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = SelectedCharacterFile != null;
        }

        private void RestoreExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            // Select backup to restore from.
            var restoreWindow = new Backups(SelectedCharacterFile) {Owner = this};
            restoreWindow.ShowDialog();

            if (restoreWindow.DialogResult == true)
            {
                // Reload profile, as user has restored it from another file.
                LoadProfile(SelectedCharacterFile);
            }
        }

        private async Task ShowNotification(string notifyText)
        {
            NotifyText.Text = notifyText;
            NotificationBorder.Opacity = 1;
            NotificationBorder.Visibility = Visibility.Visible;
            
            await Task.Delay(2000);
            var opacityAnim = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromSeconds(0.3)));
            opacityAnim.FillBehavior = FillBehavior.Stop;
            opacityAnim.Completed += (sender, args) => NotificationBorder.Visibility = Visibility.Hidden;
            NotificationBorder.BeginAnimation(OpacityProperty, opacityAnim);
            
        }

        private void itemSearch_TextChanged(object sender, TextChangedEventArgs e) 
            => InventoryViewSource.View.Refresh();

        private void InventoryViewSource_OnFilter(object sender, FilterEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ItemSearch.Text))
                e.Accepted = true;
            else if (e.Item is InventoryListItem item)
            {
                string[] filterItems = ItemSearch.Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                e.Accepted = filterItems.Any(filterItem =>
                    item.Name.Contains(filterItem, StringComparison.OrdinalIgnoreCase));
            }
        }

        private void ModifyAllSkillsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ModifyAllSkillsText.Text = $"{(e.NewValue > 0 ? "+" : "")}{e.NewValue:F0}%";
            ModifyAllSkillsButton.IsEnabled = e.NewValue != 0d;
        }

        private void ModifyAllSkillsReset_Clicked(object sender, RoutedEventArgs e)
        {
            ModifyAllSkillsSlider.Value = 5;
        }

        private void ChkLoadBackupFiles_Checked(object sender, RoutedEventArgs e)
        {
            RefreshCharacterFiles(true);
        }

        private void ChkLoadBackupFiles_Unchecked(object sender, RoutedEventArgs e)
        {
            RefreshCharacterFiles(false);
        }
    }
}
