using System.ComponentModel.DataAnnotations;

namespace Travel.Domain.Enums
{
    /// <summary>
    /// How an expense type divides its cost across an arrival group.
    ///
    /// <para>
    /// The two are not symmetric, deliberately. <see cref="PerRoom"/> takes a <b>rate per room per
    /// night</b> and multiplies <i>up</i> to a total. <see cref="PerPax"/> takes one <b>lump-sum total</b>
    /// for the whole expense and divides it <i>down</i> across the eligible passengers — which is what
    /// the operator is actually handed by a supplier in each case.
    /// </para>
    /// </summary>
    public enum ExpenseBasis
    {
        [Display(Name = "Per room")]
        PerRoom = 1,

        [Display(Name = "Per pax")]
        PerPax = 2,
    }
}
