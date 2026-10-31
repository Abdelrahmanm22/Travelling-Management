using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain;
using Travel.Domain.Authorization;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Specifications;
using Travel.Domain.Specifications.Expenses;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    /// <summary>
    /// The expense-type catalogue — the TourController shape, with the per-room/per-pax rule in place
    /// of a price. Rates are never stored here: they are typed each time a type is applied, because
    /// they change per supplier and per season.
    /// </summary>
    [Authorize]
    public class ExpenseTypeController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILogger<ExpenseTypeController> _logger;

        public ExpenseTypeController(IUnitOfWork unitOfWork, IMapper mapper, ILogger<ExpenseTypeController> logger)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _logger = logger;
        }

        [Authorize(Policy = Permissions.ExpenseTypes.View)]
        public async Task<IActionResult> Index()
        {
            var spec = new BaseSpecifications<ExpenseType>();
            spec.SetOrderBy(t => t.Name);
            var types = await _unitOfWork.Repository<ExpenseType>().GetAllAsync(spec);
            return View(types);
        }

        [Authorize(Policy = Permissions.ExpenseTypes.Create)]
        public IActionResult Create() => View(new ExpenseTypeViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.ExpenseTypes.Create)]
        public async Task<IActionResult> Create(ExpenseTypeViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var type = _mapper.Map<ExpenseType>(model);
            ApplyBasisRules(type);

            await _unitOfWork.Repository<ExpenseType>().AddAsync(type);
            await _unitOfWork.CompleteAsync();
            _logger.LogInformation("Expense type #{ExpenseTypeId} \"{ExpenseTypeName}\" created ({Basis})", type.Id, type.Name, type.Basis);

            TempData["Success"] = "Expense type created successfully.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Policy = Permissions.ExpenseTypes.Edit)]
        public async Task<IActionResult> Edit(int id)
        {
            var type = await _unitOfWork.Repository<ExpenseType>()
                .GetByIdAsync(new BaseSpecifications<ExpenseType>(t => t.Id == id));

            if (type is null)
                return NotFound();

            return View(_mapper.Map<ExpenseTypeViewModel>(type));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.ExpenseTypes.Edit)]
        public async Task<IActionResult> Edit(ExpenseTypeViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var type = await _unitOfWork.Repository<ExpenseType>()
                .GetByIdAsync(new BaseSpecifications<ExpenseType>(t => t.Id == model.Id));

            if (type is null)
                return NotFound();

            // Editing the catalogue never restates an expense already paid: BlockExpense snapshots the
            // name, the basis and both include flags when it is created, and nothing here writes them.
            _mapper.Map(model, type);
            ApplyBasisRules(type);

            _unitOfWork.Repository<ExpenseType>().Update(type);
            await _unitOfWork.CompleteAsync();
            _logger.LogInformation("Expense type #{ExpenseTypeId} \"{ExpenseTypeName}\" edited", type.Id, type.Name);

            TempData["Success"] = "Expense type updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.ExpenseTypes.Delete)]
        public async Task<IActionResult> Delete(int id)
        {
            var type = await _unitOfWork.Repository<ExpenseType>()
                .GetByIdAsync(new BaseSpecifications<ExpenseType>(t => t.Id == id));

            if (type is null)
            {
                _logger.LogWarning("Delete refused: expense type #{ExpenseTypeId} not found", id);
                return Json(new { success = false, message = "Expense type not found." });
            }

            // The FK is Restrict, so this guard is what turns an in-use delete into a readable message
            // instead of a raw SQL constraint error. Retiring is IsActive = false.
            var inUse = await _unitOfWork.Repository<BlockExpense>()
                .GetAllAsync(BlockExpenseSpec.UsingType(id));

            if (inUse.Any())
            {
                _logger.LogWarning("Delete refused: expense type #{ExpenseTypeId} \"{ExpenseTypeName}\" is used on expenses", id, type.Name);
                return Json(new
                {
                    success = false,
                    message = "This expense type has been used on one or more expenses and can't be deleted. Mark it inactive instead."
                });
            }

            _unitOfWork.Repository<ExpenseType>().Delete(type);
            await _unitOfWork.CompleteAsync();
            _logger.LogInformation("Expense type #{ExpenseTypeId} \"{ExpenseTypeName}\" deleted", id, type.Name);

            return Json(new { success = true, message = "Expense type deleted successfully." });
        }

        // View, not Create — the Edit form calls this too.
        [AcceptVerbs("GET", "POST")]
        [Authorize(Policy = Permissions.ExpenseTypes.View)]
        public async Task<IActionResult> IsNameAvailable(string name, int id)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Json(true);

            var matches = await _unitOfWork.Repository<ExpenseType>()
                .GetAllAsync(new BaseSpecifications<ExpenseType>(t => t.Name == name && t.Id != id));

            return Json(!matches.Any());
        }

        /// <summary>
        /// The include flags belong to per-pax only. Clearing them on a per-room save stops a stale
        /// setting resurfacing if the type is switched to per pax later — the same call the booking
        /// form makes when it nulls a price sitting behind a zero count.
        /// </summary>
        private static void ApplyBasisRules(ExpenseType type)
        {
            if (type.Basis != ExpenseBasis.PerRoom)
                return;

            type.IncludeChildren = false;
            type.IncludeInfants = false;
        }
    }
}
