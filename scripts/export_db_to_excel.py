#!/usr/bin/env python3
"""
Read-only SQLite -> formatted Excel (.xlsx) exporter for SWARNABHOOMI.

Reads backend/src/SWARNABHOOMI.Api/App_Data/swarnabhoomi.db in strict read-only mode
(?mode=ro) and writes 8 sanitized, human-readable Excel workbooks into data-exports/.
"""

from __future__ import annotations

import pathlib
import sqlite3
from dataclasses import dataclass
from typing import Any

from openpyxl import Workbook
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
from openpyxl.utils import get_column_letter

PROJECT_ROOT = pathlib.Path(__file__).resolve().parent.parent
DEFAULT_DB_PATH = PROJECT_ROOT / "backend" / "src" / "SWARNABHOOMI.Api" / "App_Data" / "swarnabhoomi.db"
EXPORT_DIR = PROJECT_ROOT / "data-exports"

EXCLUDED_TABLES = {"AiAssistantConfigurations", "__EFMigrationsHistory", "__EFMigrationsLock"}
FORBIDDEN_USER_COLUMNS = {"PasswordHash", "SecurityStamp"}

ROLE_NAMES = {
    0: "Farmer",
    1: "Buyer",
    2: "Government",
    3: "Officer",
    4: "SuperAdmin",
    5: "Developer",
}

HEADER_FILL = PatternFill(start_color="1B4332", end_color="1B4332", fill_type="solid")
HEADER_FONT = Font(name="Calibri", size=11, bold=True, color="FFFFFF")
STRIPE_FILL = PatternFill(start_color="F4F7F5", end_color="F4F7F5", fill_type="solid")
BODY_FONT = Font(name="Calibri", size=10.5, color="1F2933")
THIN_BORDER_SIDE = Side(border_style="thin", color="D9E2DC")
CELL_BORDER = Border(
    left=THIN_BORDER_SIDE,
    right=THIN_BORDER_SIDE,
    top=THIN_BORDER_SIDE,
    bottom=THIN_BORDER_SIDE,
)

DECIMAL_2_COLUMNS = {
    "AreaHectares",
    "TotalLandAreaHectares",
    "TotalAreaHectares",
    "QuantityTonnes",
    "MonthlyDemandTonnes",
    "OfferPricePerKg",
    "TotalOfferValueLkr",
    "Value",
    "RainfallMm",
    "TemperatureC",
    "HumidityPercent",
    "MinimumValue",
    "MaximumValue",
}

KG_INT_COLUMNS = {
    "AverageYieldKgPerHectare",
    "QuantityKg",
    "RequestedQuantityKg",
    "PredictedPaddyQuantityKg",
    "ActualPaddyQuantityKg",
    "LossesKg",
}

COUNT_INT_COLUMNS = {
    "MaturityDays",
    "Year",
    "Version",
    "FarmersCount",
    "FarmsCount",
    "FieldsCount",
    "VerifiedFields",
    "PendingFields",
    "RejectedFields",
}

BOOL_COLUMNS = {"IsActive", "IsVerified", "IsRead"}


@dataclass(frozen=True)
class SheetSpec:
    name: str
    query: str


def inspect_schema(conn: sqlite3.Connection) -> dict[str, set[str]]:
    tables = [
        row[0]
        for row in conn.execute(
            "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"
        )
        if row[0] not in EXCLUDED_TABLES
    ]
    schema: dict[str, set[str]] = {}
    for table in tables:
        cols = {row[1] for row in conn.execute(f'PRAGMA table_info("{table}")')}
        if table == "Users":
            cols -= FORBIDDEN_USER_COLUMNS
        schema[table] = cols
    return schema


def coerce_cell_value(col_name: str, value: Any) -> Any:
    if value is None:
        return None
    if col_name in BOOL_COLUMNS:
        return bool(value)
    if col_name in DECIMAL_2_COLUMNS or col_name in KG_INT_COLUMNS:
        try:
            return float(value)
        except (TypeError, ValueError):
            return value
    if col_name in COUNT_INT_COLUMNS:
        try:
            return int(float(value))
        except (TypeError, ValueError):
            return value
    return value


def write_styled_sheet(ws, headers: list[str], rows: list[tuple[Any, ...]]) -> None:
    ws.append(headers)

    for col_idx in range(1, len(headers) + 1):
        cell = ws.cell(row=1, column=col_idx)
        cell.fill = HEADER_FILL
        cell.font = HEADER_FONT
        cell.border = CELL_BORDER
        cell.alignment = Alignment(horizontal="left", vertical="center")
    ws.row_dimensions[1].height = 24

    for row_offset, raw_row in enumerate(rows, start=2):
        coerced = [coerce_cell_value(headers[i], raw_row[i]) for i in range(len(headers))]
        ws.append(coerced)
        is_striped = row_offset % 2 == 0
        for col_idx, col_name in enumerate(headers, start=1):
            cell = ws.cell(row=row_offset, column=col_idx)
            cell.font = BODY_FONT
            cell.border = CELL_BORDER
            if is_striped:
                cell.fill = STRIPE_FILL

            if col_name in DECIMAL_2_COLUMNS and isinstance(cell.value, (int, float)):
                cell.number_format = "#,##0.00"
                cell.alignment = Alignment(horizontal="right", vertical="center")
            elif (col_name in KG_INT_COLUMNS or col_name in COUNT_INT_COLUMNS) and isinstance(cell.value, (int, float)):
                cell.number_format = "#,##0"
                cell.alignment = Alignment(horizontal="right", vertical="center")
            else:
                cell.alignment = Alignment(horizontal="left", vertical="center")

    ws.freeze_panes = "A2"
    if rows or headers:
        last_col = get_column_letter(len(headers))
        last_row = max(1, len(rows) + 1)
        ws.auto_filter.ref = f"A1:{last_col}{last_row}"

    for col_idx, header in enumerate(headers, start=1):
        max_len = len(str(header))
        for row_idx in range(2, len(rows) + 2):
            val = ws.cell(row=row_idx, column=col_idx).value
            if val is not None:
                val_str = f"{val:,.2f}" if isinstance(val, float) else str(val)
                if len(val_str) > max_len:
                    max_len = len(val_str)
        ws.column_dimensions[get_column_letter(col_idx)].width = min(45, max(12, max_len + 3))


def role_case_sql(alias: str = "u") -> str:
    clauses = " ".join(f"WHEN {code} THEN '{label}'" for code, label in ROLE_NAMES.items())
    return f"CASE {alias}.Role {clauses} ELSE 'Unknown' END"


def build_workbook_specs() -> list[tuple[str, list[SheetSpec]]]:
    role_expr = role_case_sql("u")
    notif_role_expr = role_case_sql("u")

    return [
        (
            "01_Rice_Varieties.xlsx",
            [
                SheetSpec(
                    "RiceVarieties",
                    """
                    SELECT
                        rv.Id,
                        rv.VarietyCode,
                        rv.Name,
                        rv.MaturityDays,
                        rv.AverageYieldKgPerHectare,
                        rv.Description,
                        rv.IsActive,
                        rv.CreatedAt
                    FROM RiceVarieties rv
                    WHERE rv.IsDeleted = 0
                    ORDER BY rv.MaturityDays, rv.Name
                    """,
                ),
            ],
        ),
        (
            "02_Farmers_Farms_Fields.xlsx",
            [
                SheetSpec(
                    "Farmers",
                    """
                    SELECT
                        u.Id AS UserId,
                        u.FirstName,
                        u.LastName,
                        TRIM(COALESCE(u.FirstName, '') || ' ' || COALESCE(u.LastName, '')) AS FullName,
                        u.Email,
                        u.PhoneNumber,
                        u.District,
                        u.Province,
                        fp.NationalId,
                        fp.Address,
                        fp.TotalLandAreaHectares,
                        fp.PrimaryVariety,
                        fp.ExperienceYears,
                        u.IsActive,
                        u.IsVerified,
                        u.CreatedAt
                    FROM Users u
                    LEFT JOIN FarmerProfiles fp ON fp.UserId = u.Id AND fp.IsDeleted = 0
                    WHERE u.IsDeleted = 0 AND u.Role = 0
                    ORDER BY u.Email
                    """,
                ),
                SheetSpec(
                    "Farms",
                    """
                    SELECT
                        f.Id,
                        f.Name AS FarmName,
                        TRIM(COALESCE(u.FirstName, '') || ' ' || COALESCE(u.LastName, '')) AS FarmerName,
                        u.Email AS FarmerEmail,
                        f.District,
                        f.Province,
                        f.AreaHectares,
                        f.Address,
                        f.Latitude,
                        f.Longitude,
                        f.Description,
                        f.CreatedAt
                    FROM Farms f
                    LEFT JOIN Users u ON u.Id = f.UserId
                    WHERE f.IsDeleted = 0
                    ORDER BY f.District, f.Name
                    """,
                ),
                SheetSpec(
                    "Fields",
                    """
                    SELECT
                        fld.Id,
                        fld.Name AS FieldName,
                        fm.Name AS FarmName,
                        TRIM(COALESCE(u.FirstName, '') || ' ' || COALESCE(u.LastName, '')) AS FarmerName,
                        fm.District,
                        fm.Province,
                        fld.AreaHectares,
                        COALESCE(
                            (
                                SELECT fv.Status
                                FROM FieldVerifications fv
                                WHERE fv.FieldId = fld.Id AND fv.IsDeleted = 0
                                ORDER BY fv.CreatedAt DESC
                                LIMIT 1
                            ),
                            'Pending'
                        ) AS LatestVerificationStatus,
                        fld.Latitude,
                        fld.Longitude,
                        fld.BoundaryGeoJson,
                        fld.CreatedAt
                    FROM Fields fld
                    LEFT JOIN Farms fm ON fm.Id = fld.FarmId
                    LEFT JOIN Users u ON u.Id = fm.UserId
                    WHERE fld.IsDeleted = 0
                    ORDER BY fm.District, fm.Name, fld.Name
                    """,
                ),
                SheetSpec(
                    "FieldVerifications",
                    """
                    SELECT
                        fv.Id,
                        fv.FieldId,
                        fld.Name AS FieldName,
                        fm.Name AS FarmName,
                        TRIM(COALESCE(farmer.FirstName, '') || ' ' || COALESCE(farmer.LastName, '')) AS FarmerName,
                        fm.District,
                        fm.Province,
                        fv.Status,
                        COALESCE(
                            TRIM(COALESCE(officer.FirstName, '') || ' ' || COALESCE(officer.LastName, '')),
                            fv.ReviewerId
                        ) AS OfficerName,
                        fv.ReviewerId,
                        fv.ReviewedAt,
                        fv.ReviewNotes,
                        fv.CreatedAt
                    FROM FieldVerifications fv
                    LEFT JOIN Fields fld ON fld.Id = fv.FieldId
                    LEFT JOIN Farms fm ON fm.Id = fld.FarmId
                    LEFT JOIN Users farmer ON farmer.Id = fm.UserId
                    LEFT JOIN Users officer ON UPPER(officer.Id) = UPPER(COALESCE(fv.ReviewerId, ''))
                    WHERE fv.IsDeleted = 0
                    ORDER BY fm.District, fld.Name, fv.CreatedAt DESC
                    """,
                ),
                SheetSpec(
                    "Summary",
                    """
                    WITH LatestVerif AS (
                        SELECT
                            fv.FieldId,
                            fv.Status,
                            ROW_NUMBER() OVER (PARTITION BY fv.FieldId ORDER BY fv.CreatedAt DESC) AS rn
                        FROM FieldVerifications fv
                        WHERE fv.IsDeleted = 0
                    )
                    SELECT
                        COALESCE(fm.District, 'Unspecified') AS District,
                        COALESCE(fm.Province, 'Unspecified') AS Province,
                        COUNT(DISTINCT fm.UserId) AS FarmersCount,
                        COUNT(DISTINCT fm.Id) AS FarmsCount,
                        COUNT(fld.Id) AS FieldsCount,
                        ROUND(COALESCE(SUM(CAST(fld.AreaHectares AS REAL)), 0), 2) AS TotalAreaHectares,
                        SUM(CASE WHEN COALESCE(lv.Status, 'Pending') = 'Verified' THEN 1 ELSE 0 END) AS VerifiedFields,
                        SUM(CASE WHEN COALESCE(lv.Status, 'Pending') = 'Pending' THEN 1 ELSE 0 END) AS PendingFields,
                        SUM(CASE WHEN COALESCE(lv.Status, 'Pending') = 'Rejected' THEN 1 ELSE 0 END) AS RejectedFields
                    FROM Fields fld
                    LEFT JOIN Farms fm ON fm.Id = fld.FarmId
                    LEFT JOIN LatestVerif lv ON lv.FieldId = fld.Id AND lv.rn = 1
                    WHERE fld.IsDeleted = 0
                    GROUP BY COALESCE(fm.District, 'Unspecified'), COALESCE(fm.Province, 'Unspecified')
                    ORDER BY FieldsCount DESC, District
                    """,
                ),
            ],
        ),
        (
            "03_Buyers_and_Demands.xlsx",
            [
                SheetSpec(
                    "Buyers",
                    """
                    SELECT
                        u.Id AS UserId,
                        TRIM(COALESCE(u.FirstName, '') || ' ' || COALESCE(u.LastName, '')) AS FullName,
                        u.Email,
                        u.PhoneNumber,
                        u.District,
                        u.Province,
                        bp.BusinessName,
                        bp.BusinessRegistrationNumber,
                        bp.Address,
                        bp.ContactPerson,
                        bp.MonthlyDemandTonnes,
                        bp.PreferredVarieties,
                        u.IsActive,
                        u.IsVerified,
                        u.CreatedAt
                    FROM Users u
                    LEFT JOIN BuyerProfiles bp ON bp.UserId = u.Id AND bp.IsDeleted = 0
                    WHERE u.IsDeleted = 0 AND u.Role = 1
                    ORDER BY u.Email
                    """,
                ),
                SheetSpec(
                    "Demands",
                    """
                    SELECT
                        d.Id,
                        TRIM(COALESCE(u.FirstName, '') || ' ' || COALESCE(u.LastName, '')) AS BuyerName,
                        bp.BusinessName,
                        d.District,
                        d.Season,
                        d.PeriodStart,
                        d.PeriodEnd,
                        d.Status,
                        d.Notes,
                        d.CreatedAt
                    FROM Demands d
                    LEFT JOIN Users u ON u.Id = d.OwnerId
                    LEFT JOIN BuyerProfiles bp ON bp.UserId = u.Id AND bp.IsDeleted = 0
                    WHERE d.IsDeleted = 0
                    ORDER BY d.District, d.CreatedAt DESC
                    """,
                ),
                SheetSpec(
                    "DemandItems",
                    """
                    SELECT
                        di.Id,
                        di.DemandId,
                        TRIM(COALESCE(u.FirstName, '') || ' ' || COALESCE(u.LastName, '')) AS BuyerName,
                        bp.BusinessName,
                        d.District,
                        d.Season,
                        rv.Name AS RiceVarietyName,
                        rv.VarietyCode,
                        di.Grade,
                        di.QuantityTonnes,
                        ROUND(CAST(di.QuantityTonnes AS REAL) * 1000.0, 2) AS QuantityKg,
                        di.OfferPricePerKg,
                        di.Notes,
                        di.CreatedAt
                    FROM DemandItems di
                    LEFT JOIN Demands d ON d.Id = di.DemandId
                    LEFT JOIN Users u ON u.Id = d.OwnerId
                    LEFT JOIN BuyerProfiles bp ON bp.UserId = u.Id AND bp.IsDeleted = 0
                    LEFT JOIN RiceVarieties rv ON rv.Id = di.RiceVarietyId
                    WHERE di.IsDeleted = 0
                    ORDER BY d.District, rv.Name
                    """,
                ),
            ],
        ),
        (
            "04_Officers_and_Governance.xlsx",
            [
                SheetSpec(
                    "Officers_and_Gov",
                    f"""
                    SELECT
                        u.Id AS UserId,
                        {role_expr} AS RoleName,
                        TRIM(COALESCE(u.FirstName, '') || ' ' || COALESCE(u.LastName, '')) AS FullName,
                        u.Email,
                        u.PhoneNumber,
                        u.District,
                        u.Province,
                        gp.Institution,
                        gp.Department,
                        gp.Designation,
                        u.IsActive,
                        u.IsVerified,
                        u.CreatedAt
                    FROM Users u
                    LEFT JOIN GovernmentProfiles gp ON gp.UserId = u.Id AND gp.IsDeleted = 0
                    WHERE u.IsDeleted = 0 AND u.Role IN (2, 3, 4, 5)
                    ORDER BY u.Role, u.Email
                    """,
                ),
                SheetSpec(
                    "OfficerAssignments",
                    """
                    SELECT
                        oa.Id,
                        oa.UserId,
                        TRIM(COALESCE(u.FirstName, '') || ' ' || COALESCE(u.LastName, '')) AS OfficerName,
                        u.Email,
                        oa.Province,
                        oa.District,
                        oa.AssignedBy,
                        oa.AssignedDate,
                        oa.IsActive,
                        oa.CreatedAt
                    FROM OfficerAssignments oa
                    LEFT JOIN Users u ON u.Id = oa.UserId
                    WHERE oa.IsDeleted = 0
                    ORDER BY oa.Province, oa.District
                    """,
                ),
            ],
        ),
        (
            "05_Cultivations_and_Forecasts.xlsx",
            [
                SheetSpec(
                    "Seasons",
                    """
                    SELECT
                        s.Id,
                        s.Name,
                        s.Year,
                        s.District,
                        s.AgrarianServiceCentre,
                        s.PlantingStart,
                        s.PlantingEnd,
                        s.Status,
                        s.ApprovedBy,
                        s.ApprovedAt,
                        s.ClosedAt,
                        s.ClosureReason,
                        s.CreatedAt
                    FROM Seasons s
                    WHERE s.IsDeleted = 0
                    ORDER BY s.Year DESC, s.PlantingStart DESC
                    """,
                ),
                SheetSpec(
                    "ResourceRuleVersions",
                    """
                    SELECT
                        r.Id,
                        r.Category,
                        r.Version,
                        r.Status,
                        r.Unit,
                        r.MinimumValue,
                        r.MaximumValue,
                        r.SourceReference,
                        r.EffectiveFrom,
                        r.EffectiveTo,
                        r.ReviewedBy,
                        r.ReviewedAt,
                        r.ChangeReason,
                        r.ApplicabilityJson,
                        r.CreatedAt
                    FROM ResourceRuleVersions r
                    WHERE r.IsDeleted = 0
                    ORDER BY r.Version DESC
                    """,
                ),
                SheetSpec(
                    "CultivationRecords",
                    """
                    SELECT
                        cr.Id,
                        TRIM(COALESCE(u.FirstName, '') || ' ' || COALESCE(u.LastName, '')) AS FarmerName,
                        fm.Name AS FarmName,
                        fld.Name AS FieldName,
                        fm.District,
                        fm.Province,
                        rv.Name AS RiceVarietyName,
                        rv.VarietyCode,
                        cr.Season AS SeasonName,
                        cr.PlantingDate,
                        cr.ExpectedHarvestDate,
                        cr.AreaHectares,
                        cr.Status AS GrowthStage,
                        cr.Notes,
                        cr.CreatedAt
                    FROM CultivationRecords cr
                    LEFT JOIN Users u ON u.Id = cr.FarmerId
                    LEFT JOIN Fields fld ON fld.Id = cr.FieldId
                    LEFT JOIN Farms fm ON fm.Id = fld.FarmId
                    LEFT JOIN RiceVarieties rv ON rv.Id = cr.RiceVarietyId
                    WHERE cr.IsDeleted = 0
                    ORDER BY fm.District, cr.PlantingDate DESC
                    """,
                ),
                SheetSpec(
                    "HarvestForecasts",
                    """
                    SELECT
                        hf.Id,
                        hf.CultivationId,
                        fld.Name AS FieldName,
                        TRIM(COALESCE(u.FirstName, '') || ' ' || COALESCE(u.LastName, '')) AS FarmerName,
                        fm.District,
                        fm.Province,
                        rv.Name AS RiceVarietyName,
                        cr.Season AS SeasonName,
                        hf.TargetWindowStart,
                        hf.TargetWindowEnd,
                        hf.PredictedPaddyQuantityKg,
                        hf.ConfidenceCategory,
                        hf.MethodVersion,
                        hf.RunVersion,
                        hf.Assumptions,
                        hf.CreatedAt
                    FROM HarvestForecasts hf
                    LEFT JOIN CultivationRecords cr ON cr.Id = hf.CultivationId
                    LEFT JOIN Fields fld ON fld.Id = cr.FieldId
                    LEFT JOIN Farms fm ON fm.Id = fld.FarmId
                    LEFT JOIN Users u ON u.Id = cr.FarmerId
                    LEFT JOIN RiceVarieties rv ON rv.Id = cr.RiceVarietyId
                    WHERE hf.IsDeleted = 0
                    ORDER BY fm.District, hf.TargetWindowStart
                    """,
                ),
            ],
        ),
        (
            "06_Harvests_and_Stock.xlsx",
            [
                SheetSpec(
                    "HarvestRecords",
                    """
                    SELECT
                        hr.Id,
                        hr.CultivationId,
                        fld.Name AS FieldName,
                        fm.Name AS FarmName,
                        TRIM(COALESCE(u.FirstName, '') || ' ' || COALESCE(u.LastName, '')) AS FarmerName,
                        fm.District,
                        fm.Province,
                        rv.Name AS RiceVarietyName,
                        cr.Season AS SeasonName,
                        hr.ActualHarvestDate,
                        hr.ActualPaddyQuantityKg,
                        hr.LossesKg,
                        hr.Quality,
                        hr.VerificationStatus,
                        hr.EvidenceReference,
                        hr.CreatedAt
                    FROM HarvestRecords hr
                    LEFT JOIN CultivationRecords cr ON cr.Id = hr.CultivationId
                    LEFT JOIN Fields fld ON fld.Id = cr.FieldId
                    LEFT JOIN Farms fm ON fm.Id = fld.FarmId
                    LEFT JOIN Users u ON u.Id = cr.FarmerId
                    LEFT JOIN RiceVarieties rv ON rv.Id = cr.RiceVarietyId
                    WHERE hr.IsDeleted = 0
                    ORDER BY hr.ActualHarvestDate DESC, fm.District
                    """,
                ),
                SheetSpec(
                    "StockRecords",
                    """
                    SELECT
                        sr.Id,
                        TRIM(COALESCE(u.FirstName, '') || ' ' || COALESCE(u.LastName, '')) AS OwnerName,
                        u.Email AS OwnerEmail,
                        sr.Location AS District,
                        u.Province,
                        rv.Name AS RiceVarietyName,
                        rv.VarietyCode,
                        sr.Grade,
                        sr.CommodityStage,
                        sr.Quantity AS QuantityKg,
                        sr.Unit,
                        sr.AsOfDate,
                        sr.VerificationStatus,
                        sr.CreatedAt
                    FROM StockRecords sr
                    LEFT JOIN Users u ON UPPER(u.Id) = UPPER(COALESCE(sr.CreatedBy, ''))
                    LEFT JOIN RiceVarieties rv ON rv.Id = sr.RiceVarietyId
                    WHERE sr.IsDeleted = 0
                    ORDER BY sr.Location, rv.Name
                    """,
                ),
            ],
        ),
        (
            "07_Marketplace_Purchase_Requests.xlsx",
            [
                SheetSpec(
                    "PurchaseRequests",
                    """
                    SELECT
                        pr.Id,
                        TRIM(COALESCE(buyer.FirstName, '') || ' ' || COALESCE(buyer.LastName, '')) AS BuyerName,
                        bp.BusinessName,
                        COALESCE(
                            TRIM(COALESCE(farmer.FirstName, '') || ' ' || COALESCE(farmer.LastName, '')),
                            'Open District Pool'
                        ) AS TargetFarmerName,
                        d.District,
                        d.Season,
                        rv.Name AS RiceVarietyName,
                        rv.VarietyCode,
                        pr.QuantityKg AS RequestedQuantityKg,
                        pr.OfferPricePerKg,
                        ROUND(CAST(pr.QuantityKg AS REAL) * CAST(pr.OfferPricePerKg AS REAL), 2) AS TotalOfferValueLkr,
                        pr.Status,
                        pr.Terms,
                        pr.DeliveryTerms,
                        pr.CreatedAt,
                        pr.UpdatedAt
                    FROM PurchaseRequests pr
                    LEFT JOIN Users buyer ON buyer.Id = pr.BuyerId
                    LEFT JOIN BuyerProfiles bp ON bp.UserId = buyer.Id AND bp.IsDeleted = 0
                    LEFT JOIN Users farmer ON farmer.Id = pr.FarmerId
                    LEFT JOIN DemandItems di ON di.Id = pr.DemandItemId
                    LEFT JOIN Demands d ON d.Id = di.DemandId
                    LEFT JOIN RiceVarieties rv ON rv.Id = di.RiceVarietyId
                    WHERE pr.IsDeleted = 0
                    ORDER BY pr.CreatedAt DESC
                    """,
                ),
            ],
        ),
        (
            "08_Prices_Weather_Notifications.xlsx",
            [
                SheetSpec(
                    "MarketPriceObservations",
                    """
                    SELECT
                        mpo.Id,
                        mpo.Market,
                        rv.Name AS RiceVarietyName,
                        rv.VarietyCode,
                        mpo.Grade,
                        mpo.PriceType,
                        mpo.Value,
                        mpo.Currency,
                        mpo.Unit,
                        mpo.ObservationDate,
                        mpo.PublicationDate,
                        mpo.Source,
                        mpo.CreatedAt
                    FROM MarketPriceObservations mpo
                    LEFT JOIN RiceVarieties rv ON rv.Id = mpo.RiceVarietyId
                    WHERE mpo.IsDeleted = 0
                    ORDER BY mpo.ObservationDate DESC, mpo.Market
                    """,
                ),
                SheetSpec(
                    "WeatherObservations",
                    """
                    SELECT
                        wo.Id,
                        wo.Location,
                        wo.Latitude,
                        wo.Longitude,
                        wo.ObservationDate,
                        wo.RainfallMm,
                        wo.TemperatureC,
                        wo.HumidityPercent,
                        wo.Provider,
                        wo.CreatedAt
                    FROM WeatherObservations wo
                    WHERE wo.IsDeleted = 0
                    ORDER BY wo.ObservationDate DESC, wo.Location
                    """,
                ),
                SheetSpec(
                    "Notifications",
                    f"""
                    SELECT
                        n.Id,
                        TRIM(COALESCE(u.FirstName, '') || ' ' || COALESCE(u.LastName, '')) AS RecipientName,
                        u.Email AS RecipientEmail,
                        {notif_role_expr} AS RoleName,
                        n.Type,
                        n.MessageKey,
                        n.Payload,
                        n.IsRead,
                        n.ReadAt,
                        n.DeliveryStatus,
                        n.CreatedAt
                    FROM Notifications n
                    LEFT JOIN Users u ON u.Id = n.UserId
                    WHERE n.IsDeleted = 0
                    ORDER BY n.CreatedAt DESC
                    """,
                ),
                SheetSpec(
                    "AuditLogs",
                    """
                    SELECT
                        al.Id,
                        al.Actor,
                        al.Action,
                        al.EntityType,
                        al.EntityId,
                        al.Timestamp,
                        al.Result,
                        al.Reason,
                        al.SafeMetadata,
                        al.CreatedAt
                    FROM AuditLogs al
                    WHERE al.IsDeleted = 0
                    ORDER BY al.Timestamp DESC
                    """,
                ),
            ],
        ),
    ]


def export_all(db_path: pathlib.Path = DEFAULT_DB_PATH, output_dir: pathlib.Path = EXPORT_DIR) -> list[tuple[pathlib.Path, list[tuple[str, int]]]]:
    if not db_path.exists():
        raise FileNotFoundError(f"SQLite database not found at: {db_path}")

    output_dir.mkdir(parents=True, exist_ok=True)
    ro_uri = f"{db_path.resolve().as_uri()}?mode=ro"

    results: list[tuple[pathlib.Path, list[tuple[str, int]]]] = []
    with sqlite3.connect(ro_uri, uri=True) as conn:
        schema = inspect_schema(conn)
        missing_expected = [
            t
            for t in (
                "RiceVarieties",
                "Users",
                "Farms",
                "Fields",
                "FieldVerifications",
                "Buyers" if False else "BuyerProfiles",
                "Demands",
                "DemandItems",
                "OfficerAssignments",
                "Seasons",
                "ResourceRuleVersions",
                "CultivationRecords",
                "HarvestForecasts",
                "HarvestRecords",
                "StockRecords",
                "PurchaseRequests",
                "MarketPriceObservations",
                "WeatherObservations",
                "Notifications",
                "AuditLogs",
            )
            if t not in schema
        ]
        if missing_expected:
            raise RuntimeError(f"Missing expected SQLite tables: {missing_expected}")

        for filename, sheet_specs in build_workbook_specs():
            wb = Workbook()
            # Remove default blank sheet once we populate real sheets
            default_ws = wb.active
            sheet_counts: list[tuple[str, int]] = []

            for spec in sheet_specs:
                cursor = conn.execute(spec.query)
                headers = [desc[0] for desc in cursor.description]
                rows = cursor.fetchall()
                ws = wb.create_sheet(title=spec.name)
                write_styled_sheet(ws, headers, rows)
                sheet_counts.append((spec.name, len(rows)))

            wb.remove(default_ws)
            out_path = output_dir / filename
            wb.save(out_path)
            results.append((out_path, sheet_counts))

    return results


def main() -> None:
    results = export_all()
    print(f"Exported {len(results)} Excel workbooks to {EXPORT_DIR} (read-only SQLite: {DEFAULT_DB_PATH}):")
    for path, sheet_counts in results:
        summary = ", ".join(f"{name}={count}" for name, count in sheet_counts)
        print(f"  {path.name} ({path.stat().st_size:,} bytes): {summary}")


if __name__ == "__main__":
    main()
