# AgriERP — Item Groups (Step 8)

Products became Items, and the item form stopped being a fixed layout. Each
group now decides what the form asks. Companion to `database/scripts/14`–`17`.

---

## 1. What runs now

The **Products** module is **Items**, **Categories** are **Item Sub Groups**,
and above them sits a new **Item Groups** screen. Four groups ship:

| Group | Code prefix | Asks for, over the shared fields |
|---|---|---|
| Product Master | `P-` | Technical name, CIB licence, antidote, pre-harvest interval |
| Fertilizers Master | `F-` | N-P-K, form, subsidy scheme |
| Seed Master | `S-` | Variety, germination %, lot number, treatment, season |
| Other Master | `R-` | Warranty, model number |

Choosing a group on the item form swaps in that group's fields and filters the
sub-group list to that group. A seed is asked for its germination percentage
where a pesticide is asked for a licence number, and neither is shown the
other's questions.

**Five tables carry this.** The first three are the master layer, renamed from
their `Products`/`Categories` originals; the last two are new.

```
ItemGroupMaster        the KIND of item        (4 rows)
   │  ItemGroupId
   ├──< ItemGroupFieldMaster   which fields that kind shows, and how
   │        │  ItemGroupFieldId
   │        │
   ├──< ItemSubGroupMaster     the finer class within a group (16 rows)
   │        │  ItemSubGroupId
   │        ▼
   └──< ItemMaster             the items themselves
            │  ItemId
            ▼
         ItemMasterDetails     values for fields with no column of their own
            (ItemId + ItemGroupFieldId → FieldValue)
```

An item takes its group from its sub-group, and its code from the group's own
series. `ItemGroupFieldMaster` is the form definition: one row per field per
group, carrying the label, the control type, the order, the section, the
validation bounds, and — for a dropdown — the name of a lookup list.

---

## 2. The idea, and where it came from

This pattern is lifted from a production system (IndusDemo's `ItemMaster` /
`ItemGroupMaster` / `ItemGroupFieldMaster`), which drives a paper-trade ERP the
same way: one item table with every column any group could need, and a field
table deciding which columns each group exposes. AgriERP borrows the shape and
**departs from it in three places, each on purpose.** The departures are the
part worth reading.

### 2.1 A value is stored once, not twice

`ItemGroupFieldMaster.IsStoredOnItem` decides where a field's value lives:

- **`1`** — in the `ItemMaster` **column** named by `FieldName`. Everything
  billing, FEFO and GST reporting compute with is stored this way, keeping its
  real type, its `CHECK` constraints and its foreign keys. `SellingRate` stays
  `decimal(18,4)`; `GstSlabId` stays a real FK.
- **`0`** — in `ItemMasterDetails` as text, keyed by field id. Only the
  group-specific extras land here — seed germination, fertilizer N-P-K —
  because a typed column for them would be `NULL` for three groups in four.

The source system stores **every** field both ways: a typed column *and* a text
copy in its details table. In that database, 177 paper items × 30 fields is
**5,310 duplicated rows**, with nothing keeping the two copies of each value in
step. Here there is one home for each value and `IsStoredOnItem` names it.

### 2.2 The value-to-definition link is a real foreign key

`ItemMasterDetails.ItemGroupFieldId` is a genuine `FK` to
`ItemGroupFieldMaster`. In the source system the equivalent column is called
`FieldID` and is **`0` on every one of its 51,694 rows** — the only thing tying
a stored value back to its definition is the field **name**, matched as a
string. Rename a field there and every value already saved is silently orphaned.
Here the database refuses to orphan a value, and renaming a field cannot break
the link.

### 2.3 A lookup is a whitelisted name, never SQL

A dropdown field carries `LookupSource = 'units'` — a *name* the API resolves
against a fixed list (`subgroups`, `companies`, `units`, `gstslabs`, `hsncodes`,
`locations`, `fertilizerform`, `season`). The source system stores the actual
`SELECT` statement in that column and concatenates it at runtime. That is
unversioned code living in data, and an injection surface: a write to one row
becomes arbitrary SQL on the next page load. A name checked against a whitelist
cannot do that; a definition naming an unknown lookup degrades to a plain text
box rather than a dead control.

---

## 3. One code series per group

Each group has its own running number, so the code says what the item is at a
glance:

```
Product Master      P-000001, P-000002, ...
Fertilizers Master  F-000001, ...
Seed Master         S-000001, ...
Other Master        R-000001, ...
```

**Other is `R`, not `O`.** A capital O beside a zero in a six-digit code is the
classic misread on a handwritten stock sheet, and `P-000001` / `O-000001` differ
by a single glyph that most counter-printer fonts render nearly identically.

The seven items that predated this change were **recoded** onto their group
series (`14`→`16`). That was safe **only because** `sal.SalesDetails` and every
other transaction line join on `ItemId`, never `ItemCode` — so no document was
orphaned and no total moved. **It stops being safe** once bills carrying the old
code have gone out to customers; at that point history should be left alone and
only new items take the new form. The migration script says so at its head.

---

## 4. What the API exposes

| Endpoint | Returns |
|---|---|
| `GET /api/item-groups` | The four groups, with sub-group and item counts |
| `GET /api/item-groups/{id}/form` | The form definition: fields in draw order, sections, lookups, bounds |
| `GET /api/items` … | Renamed from `/api/products`; unchanged otherwise |
| `GET /api/item-subgroups` … | Renamed from `/api/categories` |

The form endpoint is what the client renders from. Nothing about "seeds have a
germination percentage" is written in the frontend — it draws whatever the
chosen group declares, in the order and sections the definition gives.

An item's group-specific answers travel keyed by `ItemGroupFieldId`, the same
key the form renders and posts under, so an edit round-trips with no name
matching anywhere in it.

---

## 5. What is deliberately not done

Stated plainly, because an undeclared gap reads as coverage:

- **Field definitions are read-only through the app.** They are schema-shaped
  data — adding one changes what the application stores and in which column — so
  they are edited by a reviewable, re-runnable migration, not through a screen
  that lets a busy shopkeeper delete the GST field on a Tuesday. The Item Groups
  screen shows the definitions; it does not edit them.

- **The shared fields still render statically.** Name, rates, GST and stock are
  the same for all four groups today, so the item form lays them out in code and
  only the group-specific half is definition-driven. Setting `IsDisplay = 0` on
  a *shared* field in the table therefore has no effect yet — that field is not
  read from the definition. Making the whole form definition-driven is a clean
  follow-on; it was not needed to make the four groups differ.

- **Permission codes were left unchanged.** The C# constants are `Permissions.Item`
  and `Permissions.ItemSubGroup`, but their *values* are still `"Product.View"`,
  `"Category.View"` and so on. A permission code is an identifier already written
  into `sec.Permissions` and into every role's grant list; renaming it would mean
  migrating those rows in lockstep, and a half-done migration locks users out of
  screens they had. The codes carry no meaning beyond being unique, so they stay.

---

## 6. The migration path

Scripts `00`–`13` still build the **old** shape, so a fresh install runs
`00`→`17` in order and lands exactly where this database is:

| Script | Does |
|---|---|
| `14_ItemRestructure.sql` | Renames Products→Items, Categories→Item Sub Groups — tables, columns, and ~125 constraints/indexes. `sp_rename`, so every FK and index survives and no row is copied. |
| `15_ItemGroups.sql` | Creates `ItemGroupMaster`, `ItemGroupFieldMaster`, `ItemMasterDetails`; seeds the four groups and their fields; back-fills every sub-group and item with a group. |
| `16_ItemCodeSeries.sql` | Single-letter prefixes; recodes existing items onto their group series; retires the shared `Product` series. |
| `17_ItemModules.sql` | Points the database-driven sidebar at the renamed routes and adds the Item Groups menu row. |

Each is idempotent and ends with a self-check that fails loudly rather than half-
applying. A backup was taken before `14` ran against the live database.

---

## 7. Status

Verified by `tests/api/item-groups-smoke.ps1` (18 checks) and
`scripts/item-form-check.mjs` (the form changing with the group, driven in a
browser), both green in the one-command run. See `docs/07-testing.md`.
