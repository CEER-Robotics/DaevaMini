using System.Collections.Generic;
using DaevaMini.Models;

namespace DaevaMini.Services;

public interface ICocktailRepository
{
    IReadOnlyList<Cocktail> GetAll();
}
