using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Travel.Presentation.ViewModels
{
    public class RoleViewModel
    {
        public string? Id { get; set; }

        [Required(ErrorMessage = "Role name is required.")]
        [StringLength(64, ErrorMessage = "Role name can't be longer than 64 characters.")]
        [Display(Name = "Role name")]
        [Remote(action: "IsNameAvailable", controller: "Role", AdditionalFields = nameof(Id),
            ErrorMessage = "A role with this name already exists.")]
        public string Name { get; set; } = string.Empty;

        public bool IsProtected { get; set; }

        /// <summary>Posted by the permission checkboxes. Initialised so an empty post means "clear all".</summary>
        public List<string> SelectedPermissions { get; set; } = new();

        /// <summary>Rebuilt server-side on every render; never posted.</summary>
        public List<PermissionGroupViewModel> Groups { get; set; } = new();

        public int TotalPermissionCount => Groups.Sum(g => g.Items.Count);

        public int SelectedPermissionCount => Groups.Sum(g => g.Items.Count(i => i.IsSelected));
    }

    public class PermissionGroupViewModel
    {
        public string Key { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string Icon { get; set; } = "bx-shield";

        public List<PermissionItemViewModel> Items { get; set; } = new();

        public int SelectedCount => Items.Count(i => i.IsSelected);

        public int SelectableCount => Items.Count(i => !i.IsLocked);

        public int SelectedSelectableCount => Items.Count(i => !i.IsLocked && i.IsSelected);

        /// <summary>False when every permission in the module is role-inherited — hides the bulk link.</summary>
        public bool HasSelectable => SelectableCount > 0;

        /// <summary>Drives the initial SELECT ALL / DESELECT ALL label; JS keeps it in sync after that.</summary>
        public bool AllSelectableSelected => SelectableCount > 0 && SelectedSelectableCount == SelectableCount;
    }

    public class PermissionItemViewModel
    {
        public string Value { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string? Description { get; set; }

        public bool IsSelected { get; set; }

        /// <summary>Granted by a role — rendered checked and disabled, so it never posts back.</summary>
        public bool IsLocked { get; set; }

        public string? LockedReason { get; set; }

        /// <summary>Inherited from a role but also stored as a direct grant.</summary>
        public bool IsAlsoDirect { get; set; }

        public string ElementId => "perm-" + Value.Replace('.', '-');
    }
}
