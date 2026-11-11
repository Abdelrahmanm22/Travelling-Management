using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain;
using Travel.Domain.Authorization;
using Travel.Domain.Models;
using Travel.Domain.Specifications;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    /// <summary>
    /// The movement-type catalogue — the <see cref="TourController"/> shape with a flight leg instead of
    /// a price. The leg is locked once a movement uses the type (the <c>MoneySafe.Currency</c> pattern).
    /// </summary>
    [Authorize]
    public class MovementTypeController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILogger<MovementTypeController> _logger;

        public MovementTypeController(IUnitOfWork unitOfWork, IMapper mapper, ILogger<MovementTypeController> logger)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _logger = logger;
        }

        [Authorize(Policy = Permissions.MovementTypes.View)]
        public async Task<IActionResult> Index()
        {
            var spec = new BaseSpecifications<MovementType>();
            spec.SetOrderBy(t => t.Name);
            var types = await _unitOfWork.Repository<MovementType>().GetAllAsync(spec);
            return View(types);
        }

        [Authorize(Policy = Permissions.MovementTypes.Create)]
        public IActionResult Create() => View(new MovementTypeViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.MovementTypes.Create)]
        public async Task<IActionResult> Create(MovementTypeViewModel model)
        {
            ValidateFlight(model);
            if (!ModelState.IsValid)
                return View(model);

            var type = _mapper.Map<MovementType>(model);
            type.FlightLeg = model.ToLeg();
            await _unitOfWork.Repository<MovementType>().AddAsync(type);
            await _unitOfWork.CompleteAsync();
            _logger.LogInformation("Movement type #{TypeId} \"{TypeName}\" created ({FlightLeg})",
                type.Id, type.Name, type.FlightLeg?.ToString() ?? "not a flight");

            TempData["Success"] = "Movement type created successfully.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Policy = Permissions.MovementTypes.Edit)]
        public async Task<IActionResult> Edit(int id)
        {
            var type = await LoadAsync(id);
            if (type is null)
                return NotFound();

            var model = _mapper.Map<MovementTypeViewModel>(type);
            model.IsInUse = await IsInUseAsync(id);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.MovementTypes.Edit)]
        public async Task<IActionResult> Edit(MovementTypeViewModel model)
        {
            var type = await LoadAsync(model.Id);
            if (type is null)
                return NotFound();

            // A locked leg isn't posted (its inputs are disabled), so restore it from the stored row
            // rather than trusting — or failing on — the post.
            model.IsInUse = await IsInUseAsync(type.Id);
            if (model.IsInUse)
            {
                model.SetLeg(type.FlightLeg);
                ModelState.Remove(nameof(MovementTypeViewModel.IsFlight));
                ModelState.Remove(nameof(MovementTypeViewModel.Scope));
                ModelState.Remove(nameof(MovementTypeViewModel.Direction));
            }
            else
            {
                ValidateFlight(model);
            }

            if (!ModelState.IsValid)
                return View(model);

            _mapper.Map(model, type);
            if (!model.IsInUse)
                type.FlightLeg = model.ToLeg();

            _unitOfWork.Repository<MovementType>().Update(type);
            await _unitOfWork.CompleteAsync();
            _logger.LogInformation("Movement type #{TypeId} \"{TypeName}\" edited", type.Id, type.Name);

            TempData["Success"] = "Movement type updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.MovementTypes.Delete)]
        public async Task<IActionResult> Delete(int id)
        {
            var type = await LoadAsync(id);
            if (type is null)
            {
                _logger.LogWarning("Delete refused: movement type #{TypeId} not found", id);
                return Json(new { success = false, message = "Movement type not found." });
            }

            if (await IsInUseAsync(id))
            {
                _logger.LogWarning("Delete refused: movement type #{TypeId} is used by movements", id);
                return Json(new { success = false, message = "This type is used by recorded movements and can't be deleted. Mark it inactive instead." });
            }

            _unitOfWork.Repository<MovementType>().Delete(type);
            await _unitOfWork.CompleteAsync();
            _logger.LogInformation("Movement type #{TypeId} \"{TypeName}\" deleted", id, type.Name);

            return Json(new { success = true, message = "Movement type deleted successfully." });
        }

        // View, not Create — the Edit form calls this too.
        [AcceptVerbs("GET", "POST")]
        [Authorize(Policy = Permissions.MovementTypes.View)]
        public async Task<IActionResult> IsNameAvailable(string name, int id)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Json(true);

            var matches = await _unitOfWork.Repository<MovementType>()
                .GetAllAsync(new BaseSpecifications<MovementType>(t => t.Name == name && t.Id != id));

            return Json(!matches.Any());
        }

        // Cross-field, so not an attribute. Each message keys a field with its own validation span.
        private void ValidateFlight(MovementTypeViewModel model)
        {
            if (!model.IsFlight)
            {
                model.Scope = null;
                model.Direction = null;
                return;
            }

            if (model.Scope is null)
                ModelState.AddModelError(nameof(model.Scope), "Choose International or Domestic.");
            if (model.Direction is null)
                ModelState.AddModelError(nameof(model.Direction), "Choose Arrival or Departure.");
        }

        private Task<MovementType?> LoadAsync(int id) =>
            _unitOfWork.Repository<MovementType>().GetByIdAsync(new BaseSpecifications<MovementType>(t => t.Id == id))!;

        private async Task<bool> IsInUseAsync(int id) =>
            (await _unitOfWork.Repository<InternalMovement>()
                .GetAllAsync(new BaseSpecifications<InternalMovement>(m => m.MovementTypeId == id))).Any();
    }
}
