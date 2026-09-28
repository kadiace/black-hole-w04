using UnityEngine;

public interface IInteractable
{
    public void Interact(IInteractor interactor);
    public void Release(IInteractor interactor);
}

public interface IInteractor
{
    public Transform SnapAt { get; }
}

