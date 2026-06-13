using Microsoft.EntityFrameworkCore;

using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThereAPI.Managers;

public class ContactManager
{
    private readonly AppDbContext _db;

    public ContactManager(AppDbContext db) { _db = db; }

    public async Task<OperationResult<List<ContactResponse>>> GetContactsAsync(string userId, CancellationToken ct = default)
    {
        var contacts = await _db.Contacts
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.IsFavorite)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);

        return OperationResult<List<ContactResponse>>.Ok(
            contacts.Select(MapContact).ToList());
    }

    public async Task<OperationResult<ContactResponse>> SaveContactAsync(string userId, SaveContactRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return OperationResult<ContactResponse>.Fail("Contact name is required.");

        var contact = new Contact
        {
            UserId = userId,
            Name = request.Name.Trim(),
            Email = request.Email?.Trim(),
            Phone = request.Phone?.Trim()
        };

        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync(ct);

        return OperationResult<ContactResponse>.Ok(MapContact(contact));
    }

    public async Task<OperationResult<ContactResponse>> UpdateContactAsync(int contactId, string userId, SaveContactRequest request, CancellationToken ct = default)
    {
        var contact = await _db.Contacts
            .FirstOrDefaultAsync(c => c.Id == contactId && c.UserId == userId, ct);

        if (contact is null)
            return OperationResult<ContactResponse>.Fail("Contact not found.");

        contact.Name = request.Name.Trim();
        contact.Email = request.Email?.Trim();
        contact.Phone = request.Phone?.Trim();

        await _db.SaveChangesAsync(ct);

        return OperationResult<ContactResponse>.Ok(MapContact(contact));
    }

    public async Task<OperationResult> DeleteContactAsync(int contactId, string userId, CancellationToken ct = default)
    {
        var contact = await _db.Contacts
            .FirstOrDefaultAsync(c => c.Id == contactId && c.UserId == userId, ct);

        if (contact is null)
            return OperationResult.Fail("Contact not found.");

        _db.Contacts.Remove(contact);
        await _db.SaveChangesAsync(ct);

        return OperationResult.Ok();
    }

    public async Task<OperationResult<ContactResponse>> ToggleFavoriteAsync(int contactId, string userId, CancellationToken ct = default)
    {
        var contact = await _db.Contacts
            .FirstOrDefaultAsync(c => c.Id == contactId && c.UserId == userId, ct);

        if (contact is null)
            return OperationResult<ContactResponse>.Fail("Contact not found.");

        contact.IsFavorite = !contact.IsFavorite;
        await _db.SaveChangesAsync(ct);

        return OperationResult<ContactResponse>.Ok(MapContact(contact));
    }

    private static ContactResponse MapContact(Contact c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Email = c.Email,
        Phone = c.Phone,
        IsFavorite = c.IsFavorite
    };
}
