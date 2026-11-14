using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain;
using Travel.Domain.Authorization;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Specifications.Contracts;
using Travel.Presentation.Services.Contracts;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    /// <summary>
    /// Setup ▸ Packages ▸ Contract program — the day-by-day text, lists and notes a package's contracts
    /// start from (decisions 1, 10, 17). A page of its own rather than a card on Package/Edit, so saving
    /// one form can never discard unsaved edits in the other. Rides on the Packages permissions.
    /// </summary>
    [Authorize]
    public class PackageContractController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuthorizationService _authorization;
        private readonly ILogger<PackageContractController> _logger;

        public PackageContractController(IUnitOfWork unitOfWork, IAuthorizationService authorization, ILogger<PackageContractController> logger)
        {
            _unitOfWork = unitOfWork;
            _authorization = authorization;
            _logger = logger;
        }

        [Authorize(Policy = Permissions.Packages.View)]
        public async Task<IActionResult> Edit(int id)
        {
            var package = await LoadAsync(id);
            if (package is null) return NotFound();

            var slots = ContractProgramLegs.For(package);
            var rows = package.ContractLegs.ToDictionary(l => l.Leg);
            var outOfDate = ContractProgramLegs.IsOutOfDate(package, slots);

            static PackageContractDayInput ToInput(PackageContractDay d) =>
                new() { Title = d.Title, Body = d.Body, IsDepartureDay = d.IsDepartureDay };

            var excludes = package.ContractListItems.Where(i => i.Kind == ContractListKind.Excludes).OrderBy(i => i.SortOrder).ToList();

            var model = new PackageContractViewModel
            {
                PackageId = package.Id,
                PackageCode = package.Code,
                PackageName = package.Name,
                ContractTitle = package.ContractTitle,
                ImportantText = package.ContractImportantText,
                DocumentsText = package.ContractDocumentsText,
                Legs = slots.Select(slot =>
                {
                    rows.TryGetValue(slot.Leg, out var row);
                    // A row written for another destination belongs to the old structure — start this
                    // leg from its defaults rather than attach another place's days to it.
                    var current = row is not null && row.DestinationId == slot.DestinationId;
                    return new PackageContractLegInput
                    {
                        Leg = slot.Leg,
                        DestinationId = slot.DestinationId,
                        Label = slot.Label,
                        WrittenForNights = current ? row!.WrittenForNights : slot.DefaultNights,
                        Heading = current ? row!.Heading : null,
                        Days = (current || row is null)
                            ? package.ContractDays.Where(d => d.Leg == slot.Leg).OrderBy(d => d.SortOrder).Select(ToInput).ToList()
                            : new List<PackageContractDayInput>(),
                    };
                }).ToList(),
                FinalDays = package.ContractDays.Where(d => d.Leg == PackageContractDay.FinalLeg).OrderBy(d => d.SortOrder).Select(ToInput).ToList(),
                Includes = package.ContractListItems.Where(i => i.Kind == ContractListKind.Includes).OrderBy(i => i.SortOrder)
                    .Select(i => new PackageContractListInput { Text = i.Text }).ToList(),
                Excludes = excludes.Select(i => new PackageContractListInput { Text = i.Text }).ToList(),
                VisaLineIndex = excludes.FindIndex(i => i.IsVisaLine),
                CanEdit = (await _authorization.AuthorizeAsync(User, Permissions.Packages.Edit)).Succeeded,
                IsOutOfDate = outOfDate,
            };

            return View(model);
        }

        /// <summary>Rebuilds the whole program from the post. Leg identity comes from the package, never the post.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Packages.Edit)]
        public async Task<IActionResult> Save([FromBody] PackageContractViewModel? model)
        {
            if (model is null) return Refuse("The program couldn't be read.", null, "empty request");

            var package = await LoadAsync(model.PackageId);
            if (package is null) return Refuse("Package not found.", null, $"package #{model.PackageId} not found");

            var slots = ContractProgramLegs.For(package).ToDictionary(s => s.Leg);
            if (model.Legs.Any(l => !slots.ContainsKey(l.Leg)) || model.Legs.Select(l => l.Leg).Distinct().Count() != model.Legs.Count)
                ModelState.AddModelError(string.Empty, "The package's itinerary changed while you were editing. Reload the page.");
            if (model.VisaLineIndex >= model.Excludes.Count)
                ModelState.AddModelError(nameof(model.VisaLineIndex), "Pick the visa line again.");

            if (!ModelState.IsValid)
                return Refuse("Fix the highlighted fields before saving.", ModelState
                    .Where(kv => kv.Value?.Errors.Count > 0)
                    .ToDictionary(kv => kv.Key, kv => kv.Value!.Errors[0].ErrorMessage), package.Code);

            // Rebuilt wholesale, like the segments on Package/Edit.
            foreach (var row in package.ContractLegs.ToList()) _unitOfWork.Repository<PackageContractLeg>().Delete(row);
            foreach (var row in package.ContractDays.ToList()) _unitOfWork.Repository<PackageContractDay>().Delete(row);
            foreach (var row in package.ContractListItems.ToList()) _unitOfWork.Repository<PackageContractListItem>().Delete(row);
            package.ContractLegs.Clear();
            package.ContractDays.Clear();
            package.ContractListItems.Clear();

            package.ContractTitle = Clean(model.ContractTitle);
            package.ContractImportantText = Clean(model.ImportantText);
            package.ContractDocumentsText = Clean(model.DocumentsText);

            void AddDays(int leg, IEnumerable<PackageContractDayInput> days)
            {
                var order = 0;
                foreach (var day in days)
                    package.ContractDays.Add(new PackageContractDay
                    {
                        Leg = leg,
                        SortOrder = order++,
                        Title = day.Title.Trim(),
                        Body = day.Body?.Trim() ?? string.Empty,
                        IsDepartureDay = day.IsDepartureDay,
                    });
            }

            foreach (var leg in model.Legs)
            {
                package.ContractLegs.Add(new PackageContractLeg
                {
                    Leg = leg.Leg,
                    DestinationId = slots[leg.Leg].DestinationId,
                    WrittenForNights = leg.WrittenForNights,
                    Heading = Clean(leg.Heading),
                });
                AddDays(leg.Leg, leg.Days);
            }
            AddDays(PackageContractDay.FinalLeg, model.FinalDays);

            for (var i = 0; i < model.Includes.Count; i++)
                package.ContractListItems.Add(new PackageContractListItem { Kind = ContractListKind.Includes, SortOrder = i, Text = model.Includes[i].Text.Trim() });
            for (var i = 0; i < model.Excludes.Count; i++)
                package.ContractListItems.Add(new PackageContractListItem
                {
                    Kind = ContractListKind.Excludes,
                    SortOrder = i,
                    Text = model.Excludes[i].Text.Trim(),
                    IsVisaLine = i == model.VisaLineIndex,
                });

            await _unitOfWork.CompleteAsync();

            _logger.LogInformation("Package {PackageCode} contract program edited ({Days} days, {Lines} list lines)",
                package.Code, package.ContractDays.Count, package.ContractListItems.Count);
            return Json(new { success = true, message = "Contract program saved." });
        }

        private async Task<Package?> LoadAsync(int id) =>
            await _unitOfWork.Repository<Package>().GetByIdAsync(new PackageContractProgramSpec(id));

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private IActionResult Refuse(string message, Dictionary<string, string>? errors, string context)
        {
            _logger.LogWarning("Contract program edit rejected ({Context}): {Fields}",
                context, errors is null ? "-" : string.Join(", ", errors.Keys));
            return Json(new { success = false, message, errors });
        }
    }
}
