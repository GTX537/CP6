using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Msbb.Application.DTOs;

public interface IMenuService
{
    Task<List<MenuDto>> GetAvailableMenusAsync(string userId, string branchCode);
}
