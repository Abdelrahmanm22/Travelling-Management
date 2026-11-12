using System.ComponentModel.DataAnnotations;

namespace Travel.Domain.Enums
{
    /// <summary>Which of a package's contract lists an item belongs to.</summary>
    public enum ContractListKind
    {
        [Display(Name = "Pacote inclui")]
        Includes = 1,

        [Display(Name = "Não está incluso")]
        Excludes = 2,
    }
}
