using ApiEcommerce.Models;
using ApiEcommerce.Models.Dtos;
using ApiEcommerce.Repository;
using AutoMapper;

namespace ApiEcommerce.Service;


public class CategoryService : ICategoryService
{
  private readonly ICategoryRepository _repository;
  private readonly IMapper _mapper;

  public CategoryService(ICategoryRepository repository, IMapper mapper)
  {
    _repository = repository;
    _mapper = mapper;
  }

  public async Task<IEnumerable<CategoryDto>> GetAllAsync()
  {
    var entities = await _repository.GetAllAsync();
    return _mapper.Map<IEnumerable<CategoryDto>>(entities);
  }

  public async Task<CategoryDto?> GetByIdAsync(int id)
  {
    var entity = await _repository.GetByIdAsync(id);
    return entity is null ? null : _mapper.Map<CategoryDto>(entity);
  }

  public async Task<int> CreateAsync(CreateCategoryDto dto)
  {
    if (await _repository.NameExistsAsync(dto.Name))
    {
      throw new InvalidOperationException("Category already exists.");
    }

    var entity = _mapper.Map<Category>(dto);
    await _repository.AddAsync(entity);
    return entity.Id;
  }

  public async Task UpdateAsync(int id, CreateCategoryDto dto)
  {
    var existing = await _repository.GetByIdAsync(id);
    if (existing is null)
    {
      throw new KeyNotFoundException($"Category with id {id} not found.");
    }

    _mapper.Map(dto, existing); // map into existing entity
    await _repository.UpdateAsync(existing);
  }

  public async Task DeleteAsync(int id)
  {
    if (!await _repository.ExistsAsync(id))
    {
      throw new KeyNotFoundException($"Category with id {id} not found.");
    }

    await _repository.DeleteAsync(id);
  }
}
