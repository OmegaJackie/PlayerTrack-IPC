using System.Collections.Generic;
using Dalamud.Game.Gui.ContextMenu;
using PlayerTrack.Data;
using PlayerTrack.Domain;
using PlayerTrack.Extensions;
using PlayerTrack.Resource;

namespace PlayerTrack.Handler;

public static class ContextMenuHandler
{
    private const char PrefixChar = 'P';
    public delegate void SelectPlayerDelegate(PlayerData player, bool isCurrent);
    public static event SelectPlayerDelegate? OnSelectPlayer;

    public static void Start()
    {
        Plugin.ContextMenu.OnMenuOpened += OnMenuOpen;
    }

    public static void Restart()
    {
        Dispose();
        Start();
    }

    public static void Dispose()
    {
        Plugin.ContextMenu.OnMenuOpened -= OnMenuOpen;
    }

    private static void OnMenuOpen(IMenuOpenedArgs menuOpenedArgs)
    {
        if (!menuOpenedArgs.IsValidPlayerMenu())
            return;

        if (ServiceContext.ConfigService.GetConfig().ShowOpenInPlayerTrack)
        {
            menuOpenedArgs.AddMenuItem(new MenuItem
            {
                PrefixChar = PrefixChar,
                Name = Language.OpenPlayerTrack,
                OnClicked = OpenPlayerTrack
            });
        }
        if (ServiceContext.ConfigService.GetConfig().ShowOpenLodestone)
        {
            menuOpenedArgs.AddMenuItem(new MenuItem
            {
                PrefixChar = PrefixChar,
                Name = Language.OpenLodestone,
                OnClicked = OpenLodestone
            });
        }
        if (ServiceContext.ConfigService.GetConfig().ShowAddToCategory &&
            ServiceContext.CategoryService.GetCategories().Count > 0)
        {
            menuOpenedArgs.AddMenuItem(new MenuItem
            {
                PrefixChar = PrefixChar,
                Name = Language.AddToCategory,
                IsSubmenu = true,
                OnClicked = OpenAddToCategorySubmenu
            });
        }
    }

    private static void OpenAddToCategorySubmenu(IMenuItemClickedArgs menuItemClickedArgs)
    {
        var selectedPlayer = menuItemClickedArgs.GetPlayer();
        if (selectedPlayer == null)
            return;

        // Snapshot the categories at open time and build one entry per category.
        // The player is captured in the closure so the child click doesn't need to
        // re-resolve the target.
        var submenuItems = new List<MenuItem>();
        foreach (var category in ServiceContext.CategoryService.GetCategories())
        {
            var categoryId = category.Id;
            submenuItems.Add(new MenuItem
            {
                Name = category.Name,
                OnClicked = _ => PlayerCategoryService.AddPlayerToCategory(selectedPlayer, categoryId)
            });
        }

        menuItemClickedArgs.OpenSubmenu(Language.AddToCategory, submenuItems);
    }

    private static void OpenPlayerTrack(IMenuItemClickedArgs menuItemClickedArgs)
    {
        var selectedPlayer = menuItemClickedArgs.GetPlayer();
        if (selectedPlayer == null)
            return;

        Plugin.GameFramework.RunOnFrameworkThread(() =>
        {
            var currentPlayer = Plugin.ObjectCollection.GetPlayerByContentId(selectedPlayer.ContentId);
            if (currentPlayer != null)
                OnSelectPlayer?.Invoke(currentPlayer, true);
            else
                OnSelectPlayer?.Invoke(selectedPlayer, false);
        });
    }

    private static void OpenLodestone(IMenuItemClickedArgs menuItemClickedArgs)
    {
        var selectedPlayer = menuItemClickedArgs.GetPlayer();
        if (selectedPlayer == null)
            return;

        ServiceContext.LodestoneService.OpenLodestoneProfile(selectedPlayer.Name, selectedPlayer.HomeWorld);
    }
}
