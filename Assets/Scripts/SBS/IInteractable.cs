using UnityEngine;

public interface IInteractable
{
    public void Interact(IInteractor interactor);
}

public interface IInteractor
{
    public Transform SnapAt { get; }
}

