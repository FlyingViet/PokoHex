using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PokoHex.Core;

namespace PokoHex;

public partial class Main : Form
{
    private SaveFolder? SAV;
    private ItemInfo[] Items = GameInfo.Strings.ItemDataSource;
    private bool Loading;
    private int SelectedSlot;
    private int InventoryStart;

    public Main()
    {
        InitializeComponent();
        CB_Inventory.SelectedIndex = 0;
        Menu_Language.SelectedIndex = GameLanguage.GetLanguageIndex(GameInfo.CurrentLanguage);
        foreach (var arg in Environment.GetCommandLineArgs().AsSpan(1))
        {
            if (Directory.Exists(arg) || File.Exists(arg))
                TryOpen(arg);
        }
    }

    private bool HasChanges => SAV != null && (SAV.IsEdited || TB_Name.Text != SAV.Common.PlayerName || HasLifeCoinChanges || HasSlotChanges || Editor_Villagers.HasChanges || Editor_Map.HasChanges || Editor_Skills.HasChanges || Editor_Reactions.HasChanges || Editor_Flags.HasChanges);

    private bool HasLifeCoinChanges => SAV?.Common.HasLifeCoins == true && NUD_LifeCoins.Value != SAV.Common.LifeCoins;

    private void LoadLifeCoins(SaveFile save)
    {
        var supported = save.HasLifeCoins;
        var coins = supported ? save.LifeCoins : 0;
        NUD_LifeCoins.Maximum = Math.Max(SaveFile.MaxLifeCoins, coins);
        NUD_LifeCoins.Value = coins;
        NUD_LifeCoins.Enabled = supported;
    }

    private bool HasSlotChanges
    {
        get
        {
            if (SAV == null || CB_Item.SelectedItem is not ItemInfo item)
                return false;
            var slot = SAV.Common.Inventory[SelectedSlot];
            return slot.ItemId != item.Value || slot.Count != NUD_Count.Value;
        }
    }

    private void Menu_Open_Click(object sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog { Description = "Select the save folder containing your G00 and S00 folders", UseDescriptionForTitle = true };
        if (dialog.ShowDialog() == DialogResult.OK)
            TryOpen(dialog.SelectedPath);
    }

    private void TryOpen(string path)
    {
        if (!ConfirmDiscard())
            return;
        try
        {
            var save = SaveFolder.Read(path);
            SAV = save;
            Loading = true;
            TB_Name.Text = save.Common.PlayerName;
            LoadLifeCoins(save.Common);
            CB_Inventory.SelectedIndex = 0;
            InventoryStart = 0;
            TC_Editor.Enabled = true;
            Menu_Export.Enabled = true;
            SelectedSlot = 0;
            Loading = false;
            BindItems();
            LoadInventory();
            Editor_Villagers.LoadSave(save.Common);
            Editor_Map.LoadSave(save);
            Editor_Skills.LoadSave(save.Common);
            Editor_Reactions.LoadSave(save.Common);
            Editor_Flags.LoadSave(save.Common);
            RB_Skills.Checked = true;
            Text = $"PokoHex / {Path.GetFileName(save.Folder)}";
            UpdateStatus();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            ShowError(ex.Message);
        }
        finally { Loading = false; }
    }

    private string GetItemName(ushort id) => Items.FirstOrDefault(z => z.Value == id)?.Text ?? (id == 0 ? "(Empty)" : $"Item {id:D4}");

    private void RB_PlayerSection_CheckedChanged(object sender, EventArgs e)
    {
        Editor_Skills.Visible = RB_Skills.Checked;
        Editor_Reactions.Visible = RB_Reactions.Checked;
    }

    private void LoadInventory()
    {
        if (SAV == null)
            return;
        Loading = true;
        try
        {
            DGV_Items.Rows.Clear();
            var count = InventoryStart == 0 ? SaveLayout.MainInventoryCount : SAV.Common.Inventory.Length - InventoryStart;
            for (int i = 0; i < count; i++)
            {
                var item = SAV.Common.Inventory[InventoryStart + i];
                DGV_Items.Rows.Add(i + 1, ItemSprite.GetItemSprite(item.ItemId)!, GetItemName(item.ItemId), item.Count);
            }
            SelectedSlot = InventoryStart;
            DGV_Items.CurrentCell = DGV_Items.Rows[0].Cells[2];
        }
        finally { Loading = false; }
        LoadSlot();
    }

    private void CB_Inventory_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (Loading || SAV == null || CB_Inventory.SelectedIndex < 0)
            return;
        try
        {
            SaveSlot();
            InventoryStart = CB_Inventory.SelectedIndex == 0 ? 0 : SaveLayout.MainInventoryCount;
            LoadInventory();
        }
        catch (ArgumentException ex)
        {
            Loading = true;
            CB_Inventory.SelectedIndex = InventoryStart == 0 ? 0 : 1;
            Loading = false;
            ShowError(ex.Message);
        }
    }

    private void Menu_Language_SelectedIndexChanged(object sender, EventArgs e)
    {
        if ((uint)Menu_Language.SelectedIndex >= GameLanguage.LanguageCount)
            return;
        var itemId = (CB_Item.SelectedItem as ItemInfo)?.Value ?? 0;
        var count = NUD_Count.Value;
        GameInfo.SetLanguage2Char(Menu_Language.SelectedIndex);
        Editor_Villagers.RefreshLanguage();
        Editor_Map.RefreshLanguage();
        Editor_Skills.RefreshLanguage();
        Editor_Reactions.RefreshLanguage();
        Editor_Flags.RefreshLanguage();
        Items = GameInfo.Strings.ItemDataSource;
        BindItems();
        Loading = true;
        try
        {
            if (SAV != null)
            {
                for (int i = 0; i < DGV_Items.Rows.Count; i++)
                    DGV_Items.Rows[i].Cells[2].Value = GetItemName(SAV.Common.Inventory[InventoryStart + i].ItemId);
            }
            CB_Item.SelectedValue = itemId;
            NUD_Count.Value = count;
            L_Catalog.Text = $"{Items.Length - 1:N0} items are available";
        }
        finally { Loading = false; }
    }

    private void BindItems()
    {
        Loading = true;
        var list = Items.ToList();
        if (list.Count == 0)
            list.Add(new ItemInfo(0, "(Empty)"));
        if (SAV != null)
        {
            foreach (var id in SAV.Common.Inventory.Select(z => z.ItemId).Distinct())
            {
                if (list.All(z => z.Value != id))
                    list.Add(new ItemInfo(id, GetItemName(id)));
            }
        }
        CB_Item.DataSource = list.OrderBy(z => z.Value != 0).ThenBy(z => z.Text).ThenBy(z => z.Value).ToArray();
        CB_Item.DisplayMember = nameof(ItemInfo.Display);
        CB_Item.ValueMember = nameof(ItemInfo.Value);
        B_Set.Enabled = Items.Length != 0;
        Loading = false;
    }

    private void DGV_Items_CurrentCellChanged(object sender, EventArgs e)
    {
        if (Loading || SAV == null || DGV_Items.CurrentRow == null)
            return;
        try
        {
            SaveSlot();
            SelectedSlot = InventoryStart + DGV_Items.CurrentRow.Index;
            LoadSlot();
        }
        catch (ArgumentException ex) { ShowError(ex.Message); }
    }

    private void LoadSlot()
    {
        if (SAV == null)
            return;
        Loading = true;
        var item = SAV.Common.Inventory[SelectedSlot];
        CB_Item.SelectedValue = item.ItemId;
        NUD_Count.Value = item.Count;
        L_Slot.Text = SelectedSlot < SaveLayout.MainInventoryCount
            ? $"Slot {SelectedSlot + 1} of {SaveLayout.MainInventoryCount}"
            : $"Secondary bar slot {SelectedSlot + 1 - SaveLayout.MainInventoryCount} of {SAV.Common.Inventory.Length - SaveLayout.MainInventoryCount}";
        Loading = false;
    }

    private void SaveSlot()
    {
        if (Loading || SAV == null || CB_Item.SelectedItem is not ItemInfo item)
            return;
        if (CB_Item.Text != item.Display)
            throw new ArgumentException("Select an item from the list.");
        if (!HasSlotChanges)
            return;
        SAV.Common.Inventory[SelectedSlot].Set(item.Value, (int)NUD_Count.Value);
        RefreshSlot();
    }

    private void RefreshSlot()
    {
        if (SAV == null)
            return;
        var item = SAV.Common.Inventory[SelectedSlot];
        var row = DGV_Items.Rows[SelectedSlot - InventoryStart];
        row.Cells[1].Value = ItemSprite.GetItemSprite(item.ItemId);
        row.Cells[2].Value = GetItemName(item.ItemId);
        row.Cells[3].Value = item.Count;
        UpdateStatus();
    }

    private void B_Set_Click(object sender, EventArgs e)
    {
        try
        {
            SaveSlot();
            LoadSlot();
        }
        catch (ArgumentException ex) { ShowError(ex.Message); }
    }

    private void B_Clear_Click(object sender, EventArgs e)
    {
        if (SAV == null)
            return;
        SAV.Common.Inventory[SelectedSlot].Set(0, 0);
        RefreshSlot();
        LoadSlot();
    }

    private void CB_Item_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (Loading || CB_Item.SelectedItem is not ItemInfo item)
            return;
        if (item.Value == 0)
            NUD_Count.Value = 0;
        else if (NUD_Count.Value == 0)
            NUD_Count.Value = 1;
    }

    private void CB_Item_DrawItem(object sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0 || CB_Item.Items[e.Index] is not ItemInfo item)
            return;
        var size = e.Bounds.Height - 4;
        var image = ItemSprite.GetItemSprite(item.Value);
        if (image != null)
            e.Graphics.DrawImage(image, new Rectangle(e.Bounds.Left + 2, e.Bounds.Top + 2, size, size));
        var bounds = new Rectangle(e.Bounds.Left + size + 6, e.Bounds.Top, e.Bounds.Width - size - 6, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, item.Display, e.Font ?? CB_Item.Font, bounds, e.ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }

    private void Menu_Export_Click(object sender, EventArgs e)
    {
        if (SAV == null)
            return;
        try
        {
            ValidateChildren();
            SaveSlot();
            Editor_Villagers.SaveChanges();
            Editor_Map.SaveChanges();
            Editor_Skills.SaveChanges();
            Editor_Reactions.SaveChanges();
            Editor_Flags.SaveChanges();
            if (TB_Name.Text != SAV.Common.PlayerName)
                SAV.Common.PlayerName = TB_Name.Text;
            if (HasLifeCoinChanges)
                SAV.Common.LifeCoins = (uint)NUD_LifeCoins.Value;
            using var dialog = new FolderBrowserDialog { Description = "Select a new, empty folder for the edited save", UseDescriptionForTitle = true };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;
            SAV.Export(dialog.SelectedPath);
            SAV = SaveFolder.Read(dialog.SelectedPath);
            LoadLifeCoins(SAV.Common);
            Editor_Villagers.LoadSave(SAV.Common);
            Editor_Map.LoadSave(SAV);
            Editor_Skills.LoadSave(SAV.Common);
            Editor_Reactions.LoadSave(SAV.Common);
            Editor_Flags.LoadSave(SAV.Common);
            Text = $"PokoHex / {Path.GetFileName(SAV.Folder)}";
            LoadSlot();
            L_Status.Text = $"Exported to {dialog.SelectedPath}";
            MessageBox.Show(this, "The edited save was exported and verified.", "PokoHex", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            ShowError(ex.Message);
        }
    }

    private void TC_Editor_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (Loading || SAV == null)
            return;
        Editor_Flags.RefreshValues();
        Editor_Skills.RefreshValues();
        Editor_Reactions.RefreshValues();
    }

    private void UpdateStatus()
    {
        L_Status.Text = SAV == null ? "Open a save folder to begin." : $"Loaded: {SAV.Common.PlayerName}.";
    }

    private bool ConfirmDiscard() => !HasChanges || MessageBox.Show(this, "Discard changes to the opened save?", "PokoHex", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    private void ShowError(string message) => MessageBox.Show(this, message, "PokoHex", MessageBoxButtons.OK, MessageBoxIcon.Error);
    private void Main_FormClosing(object sender, FormClosingEventArgs e) => e.Cancel = !ConfirmDiscard();
    private void Menu_Exit_Click(object sender, EventArgs e) => Close();

    private void Main_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private void Main_DragDrop(object sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
            TryOpen(paths[0]);
    }
}
