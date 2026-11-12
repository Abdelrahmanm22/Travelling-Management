using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain;
using Travel.Domain.Authorization;
using Travel.Domain.Models;
using Travel.Domain.Specifications;
using Travel.Presentation.Services.Contracts;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    /// <summary>
    /// Setup ▸ Contract Terms — the company's standard legal text, edited here once and read live by
    /// every contract generated afterwards (decision 6). Read-only without <see cref="Permissions.ContractTerms.Edit"/>.
    /// </summary>
    [Authorize]
    public class ContractTermsController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuthorizationService _authorization;
        private readonly ILogger<ContractTermsController> _logger;

        public ContractTermsController(IUnitOfWork unitOfWork, IAuthorizationService authorization, ILogger<ContractTermsController> logger)
        {
            _unitOfWork = unitOfWork;
            _authorization = authorization;
            _logger = logger;
        }

        [Authorize(Policy = Permissions.ContractTerms.View)]
        public async Task<IActionResult> Index()
        {
            var (settings, terms) = await ContractRenderer.LoadTermsAsync(_unitOfWork);

            return View(new ContractTermsViewModel
            {
                DocumentTitle = settings.DocumentTitle,
                SignatureCity = settings.SignatureCity,
                CompanySignatureName = settings.CompanySignatureName,
                Sections = terms.Select(t => new ContractTermsSectionInput
                {
                    Placement = t.Placement,
                    Heading = t.Heading,
                    Body = t.Body,
                }).ToList(),
                CanEdit = (await _authorization.AuthorizeAsync(User, Permissions.ContractTerms.Edit)).Succeeded,
            });
        }

        /// <summary>Replaces the settings and every section wholesale — the order posted is the print order.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.ContractTerms.Edit)]
        public async Task<IActionResult> Save([FromBody] ContractTermsViewModel? model)
        {
            if (model is null)
                return Refuse("The terms couldn't be read.", null);

            foreach (var (section, i) in model.Sections.Select((s, i) => (s, i)))
                if (!Enum.IsDefined(section.Placement))
                    ModelState.AddModelError($"Sections[{i}].Placement", "Unknown placement.");

            if (!ModelState.IsValid)
                return Refuse("Fix the highlighted fields before saving.", ModelState
                    .Where(kv => kv.Value?.Errors.Count > 0)
                    .ToDictionary(kv => kv.Key, kv => kv.Value!.Errors[0].ErrorMessage));

            var settingsRepo = _unitOfWork.Repository<ContractSettings>();
            var settings = (await settingsRepo.GetAllAsync(new BaseSpecifications<ContractSettings>())).OrderBy(s => s.Id).FirstOrDefault();
            if (settings is null)
            {
                settings = new ContractSettings();
                await settingsRepo.AddAsync(settings);
            }
            settings.DocumentTitle = model.DocumentTitle.Trim();
            settings.SignatureCity = model.SignatureCity.Trim();
            settings.CompanySignatureName = model.CompanySignatureName.Trim();

            var sectionRepo = _unitOfWork.Repository<ContractTermsSection>();
            var existing = (await sectionRepo.GetAllAsync(new BaseSpecifications<ContractTermsSection>())).ToList();
            foreach (var old in existing)
                sectionRepo.Delete(old);

            var order = 0;
            foreach (var input in model.Sections.OrderBy(s => s.Placement))
            {
                await sectionRepo.AddAsync(new ContractTermsSection
                {
                    Placement = input.Placement,
                    SortOrder = order++,
                    Heading = string.IsNullOrWhiteSpace(input.Heading) ? null : input.Heading.Trim(),
                    Body = input.Body.Trim(),
                });
            }

            await _unitOfWork.CompleteAsync();

            var added = Math.Max(0, model.Sections.Count - existing.Count);
            var removed = Math.Max(0, existing.Count - model.Sections.Count);
            _logger.LogInformation("Contract terms edited ({Sections} sections, +{Added} / -{Removed})",
                model.Sections.Count, added, removed);

            return Json(new { success = true, message = "Contract terms saved. The next contract generated uses them." });
        }

        private IActionResult Refuse(string message, Dictionary<string, string>? errors)
        {
            _logger.LogWarning("Contract terms edit rejected: {Fields}",
                errors is null ? "empty request" : string.Join(", ", errors.Keys));
            return Json(new { success = false, message, errors });
        }
    }
}
