using AgriERP.Application.Common.Models;
using AgriERP.Application.Features.Masters.Dtos;
using AgriERP.Application.Features.Modules.Dtos;
using AgriERP.Application.Features.Items.Dtos;
using AgriERP.Domain.Entities.Masters;
using AgriERP.Domain.Entities.Items;
using AgriERP.Domain.Entities.System;
using AutoMapper;

namespace AgriERP.Application.Common.Mappings;

/// <summary>
/// Entity to DTO maps. These are consumed by ProjectTo, so every MapFrom
/// expression has to be translatable to SQL - no method calls that only exist
/// in .NET, no null-conditional operators.
///
/// The item LIST is projected by hand in ItemService rather than mapped
/// here: it joins vw_ItemStock for the rolled-up stock figures, and a
/// join projection is clearer written out than bent through AutoMapper.
/// </summary>
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        /* ---------------- ItemSubGroup ---------------- */
        CreateMap<ItemSubGroup, ItemSubGroupListDto>()
            .ForMember(d => d.ParentItemSubGroupName,
                       o => o.MapFrom(s => s.ParentItemSubGroup != null ? s.ParentItemSubGroup.ItemSubGroupName : null))
            .ForMember(d => d.ItemCount,
                       o => o.MapFrom(s => s.Items.Count(p => !p.IsDeleted)));

        CreateMap<ItemSubGroup, ItemSubGroupDto>()
            .IncludeBase<ItemSubGroup, ItemSubGroupListDto>();

        CreateMap<SaveItemSubGroupRequest, ItemSubGroup>()
            // Identity, audit columns and the concurrency token are owned by
            // the database and the interceptor, never by an inbound request.
            .ForMember(d => d.ItemSubGroupId, o => o.Ignore())
            .ForMember(d => d.CreatedAt, o => o.Ignore())
            .ForMember(d => d.CreatedBy, o => o.Ignore())
            .ForMember(d => d.UpdatedAt, o => o.Ignore())
            .ForMember(d => d.UpdatedBy, o => o.Ignore())
            .ForMember(d => d.RowVersion, o => o.Ignore())
            .ForMember(d => d.IsDeleted, o => o.Ignore());

        CreateMap<ItemSubGroup, LookupDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.ItemSubGroupId))
            .ForMember(d => d.Code, o => o.MapFrom(s => s.ItemSubGroupCode))
            .ForMember(d => d.Name, o => o.MapFrom(s => s.ItemSubGroupName))
            .ForMember(d => d.Description, o => o.MapFrom(s => s.Description));

        /* ---------------- Company ---------------- */
        CreateMap<Company, CompanyListDto>()
            .ForMember(d => d.StateName, o => o.MapFrom(s => s.State != null ? s.State.StateName : null))
            .ForMember(d => d.ItemCount, o => o.MapFrom(s => s.Items.Count(p => !p.IsDeleted)));

        CreateMap<Company, CompanyDto>()
            .IncludeBase<Company, CompanyListDto>();

        CreateMap<SaveCompanyRequest, Company>()
            .ForMember(d => d.CompanyId, o => o.Ignore())
            .ForMember(d => d.LogoPath, o => o.Ignore())
            .ForMember(d => d.CreatedAt, o => o.Ignore())
            .ForMember(d => d.CreatedBy, o => o.Ignore())
            .ForMember(d => d.UpdatedAt, o => o.Ignore())
            .ForMember(d => d.UpdatedBy, o => o.Ignore())
            .ForMember(d => d.RowVersion, o => o.Ignore())
            .ForMember(d => d.IsDeleted, o => o.Ignore());

        CreateMap<Company, LookupDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.CompanyId))
            .ForMember(d => d.Code, o => o.MapFrom(s => s.CompanyCode))
            .ForMember(d => d.Name, o => o.MapFrom(s => s.CompanyName))
            .ForMember(d => d.Description, o => o.MapFrom(s => s.City));

        /* ---------------- Shop Master ---------------- */
        CreateMap<ShopMaster, ShopListDto>()
            .ForMember(d => d.StateName, o => o.MapFrom(s => s.State != null ? s.State.StateName : null));

        CreateMap<ShopMaster, ShopDto>()
            .IncludeBase<ShopMaster, ShopListDto>();

        CreateMap<SaveShopRequest, ShopMaster>()
            .ForMember(d => d.ShopId, o => o.Ignore())
            .ForMember(d => d.State, o => o.Ignore())
            .ForMember(d => d.CreatedAt, o => o.Ignore())
            .ForMember(d => d.CreatedBy, o => o.Ignore())
            .ForMember(d => d.UpdatedAt, o => o.Ignore())
            .ForMember(d => d.UpdatedBy, o => o.Ignore())
            .ForMember(d => d.RowVersion, o => o.Ignore())
            .ForMember(d => d.IsDeleted, o => o.Ignore());

        /* ---------------- Warehouse Master ---------------- */
        CreateMap<WarehouseMaster, WarehouseListDto>()
            .ForMember(d => d.BinCount, o => o.MapFrom(s => s.Bins.Count));

        CreateMap<WarehouseMaster, WarehouseDto>()
            .IncludeBase<WarehouseMaster, WarehouseListDto>()
            .ForMember(d => d.Bins,
                o => o.MapFrom(s => s.Bins.OrderBy(b => b.WarehouseBinId).Select(b => b.BinName)));

        CreateMap<WarehouseMaster, LookupDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.WarehouseId))
            .ForMember(d => d.Code, o => o.MapFrom(s => s.WarehouseCode))
            .ForMember(d => d.Name, o => o.MapFrom(s => s.WarehouseName))
            .ForMember(d => d.Description, o => o.MapFrom(s => s.Address));

        /* ---------------- Supplier ---------------- */
        CreateMap<Supplier, SupplierListDto>()
            .ForMember(d => d.StateName, o => o.MapFrom(s => s.State != null ? s.State.StateName : null));

        CreateMap<Supplier, SupplierDto>()
            .IncludeBase<Supplier, SupplierListDto>()
            // Filled from vw_SupplierOutstanding by the service; it is not
            // a column on the entity.
            .ForMember(d => d.OutstandingAmount, o => o.Ignore());

        CreateMap<SaveSupplierRequest, Supplier>()
            .ForMember(d => d.SupplierId, o => o.Ignore())
            .ForMember(d => d.SupplierCode, o => o.Ignore())   // allocated by the number series
            .ForMember(d => d.CreatedAt, o => o.Ignore())
            .ForMember(d => d.CreatedBy, o => o.Ignore())
            .ForMember(d => d.UpdatedAt, o => o.Ignore())
            .ForMember(d => d.UpdatedBy, o => o.Ignore())
            .ForMember(d => d.RowVersion, o => o.Ignore())
            .ForMember(d => d.IsDeleted, o => o.Ignore());

        CreateMap<Supplier, LookupDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.SupplierId))
            .ForMember(d => d.Code, o => o.MapFrom(s => s.SupplierCode))
            .ForMember(d => d.Name, o => o.MapFrom(s => s.SupplierName))
            .ForMember(d => d.Description, o => o.MapFrom(s => s.City));

        /* ---------------- Customer ---------------- */
        CreateMap<Customer, CustomerListDto>();

        CreateMap<Customer, CustomerDto>()
            .IncludeBase<Customer, CustomerListDto>()
            .ForMember(d => d.StateName, o => o.MapFrom(s => s.State != null ? s.State.StateName : null))
            .ForMember(d => d.OutstandingAmount, o => o.Ignore())
            .ForMember(d => d.LastInvoiceDate, o => o.Ignore())
            .ForMember(d => d.OldestUnpaidAgeDays, o => o.Ignore());

        CreateMap<SaveCustomerRequest, Customer>()
            .ForMember(d => d.CustomerId, o => o.Ignore())
            .ForMember(d => d.CustomerCode, o => o.Ignore())    // allocated by the number series
            .ForMember(d => d.CreatedAt, o => o.Ignore())
            .ForMember(d => d.CreatedBy, o => o.Ignore())
            .ForMember(d => d.UpdatedAt, o => o.Ignore())
            .ForMember(d => d.UpdatedBy, o => o.Ignore())
            .ForMember(d => d.RowVersion, o => o.Ignore())
            .ForMember(d => d.IsDeleted, o => o.Ignore());

        CreateMap<Customer, LookupDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.CustomerId))
            .ForMember(d => d.Code, o => o.MapFrom(s => s.CustomerCode))
            .ForMember(d => d.Name, o => o.MapFrom(s => s.CustomerName))
            // Village disambiguates the four Ramesh Patils in the customer list.
            .ForMember(d => d.Description, o => o.MapFrom(s => s.Village));

        /* ---------------- Unit ---------------- */
        CreateMap<Unit, UnitDto>();

        CreateMap<SaveUnitRequest, Unit>()
            .ForMember(d => d.UnitId, o => o.Ignore())
            .ForMember(d => d.CreatedAt, o => o.Ignore())
            .ForMember(d => d.CreatedBy, o => o.Ignore())
            .ForMember(d => d.UpdatedAt, o => o.Ignore())
            .ForMember(d => d.UpdatedBy, o => o.Ignore())
            .ForMember(d => d.RowVersion, o => o.Ignore())
            .ForMember(d => d.IsDeleted, o => o.Ignore());

        CreateMap<Unit, LookupDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.UnitId))
            .ForMember(d => d.Code, o => o.MapFrom(s => s.UnitCode))
            .ForMember(d => d.Name, o => o.MapFrom(s => s.UnitName));

        /* ---------------- Reference lookups ---------------- */
        CreateMap<State, LookupDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.StateId))
            .ForMember(d => d.Code, o => o.MapFrom(s => s.StateCode))
            .ForMember(d => d.Name, o => o.MapFrom(s => s.StateName))
            .ForMember(d => d.Description, o => o.MapFrom(s => s.StateAbbr));

        CreateMap<GstSlab, LookupDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.GstSlabId))
            .ForMember(d => d.Code, o => o.MapFrom(s => s.SlabName))
            .ForMember(d => d.Name, o => o.MapFrom(s => s.SlabName));

        CreateMap<HsnCode, LookupDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.HsnId))
            .ForMember(d => d.Code, o => o.MapFrom(s => s.Code))
            .ForMember(d => d.Name, o => o.MapFrom(s => s.Code))
            .ForMember(d => d.Description, o => o.MapFrom(s => s.Description));

        CreateMap<StorageLocation, LookupDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.LocationId))
            .ForMember(d => d.Code, o => o.MapFrom(s => s.LocationCode))
            .ForMember(d => d.Name, o => o.MapFrom(s => s.LocationName));

        /* ---------------- Sidebar module ---------------- */
        // Grouping and the head columns are handled in ModuleService; this map
        // only flattens the leaf entry the sidebar renders.
        CreateMap<ModuleMaster, ModuleDto>();

        /* ---------------- Item ---------------- */
        CreateMap<SaveItemRequest, Item>()
            .ForMember(d => d.ItemId, o => o.Ignore())
            .ForMember(d => d.ItemCode, o => o.Ignore())     // allocated by the number series
            .ForMember(d => d.CreatedAt, o => o.Ignore())
            .ForMember(d => d.CreatedBy, o => o.Ignore())
            .ForMember(d => d.UpdatedAt, o => o.Ignore())
            .ForMember(d => d.UpdatedBy, o => o.Ignore())
            .ForMember(d => d.RowVersion, o => o.Ignore())
            .ForMember(d => d.IsDeleted, o => o.Ignore());

        CreateMap<ItemBatch, ItemBatchDto>()
            .ForMember(d => d.LocationName, o => o.MapFrom(s => s.Location != null ? s.Location.LocationName : string.Empty))
            // Computed in the service after materialisation: DATEDIFF against
            // "today" is a per-row decision, not something to bake into a
            // projection that might be cached.
            .ForMember(d => d.DaysToExpiry, o => o.Ignore())
            .ForMember(d => d.ExpiryStatus, o => o.Ignore());
    }
}
