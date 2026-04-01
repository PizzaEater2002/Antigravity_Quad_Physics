namespace Odal.Core
{
    /// <summary>
    /// Интерфейс для объектов, требующих обновления в цикле физики.
    /// Заменяет стандартный MonoBehaviour FixedUpdate().
    /// </summary>
    public interface IFixedUpdatable
    {
        /// <summary>
        /// Метод вызывается каждый физический кадр менеджером обновления.
        /// </summary>
        /// <param name="fixedDeltaTime">Фиксированное время шага физики</param>
        void FixedTick(float fixedDeltaTime);
    }
}
