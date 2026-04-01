namespace Odal.Core
{
    /// <summary>
    /// Интерфейс для объектов, требующих покадрового обновления.
    /// Заменяет стандартный MonoBehaviour Update().
    /// </summary>
    public interface IUpdatable
    {
        /// <summary>
        /// Метод вызывается каждый кадр менеджером обновления.
        /// </summary>
        /// <param name="deltaTime">Время, прошедшее с предыдущего кадра</param>
        void Tick(float deltaTime);
    }
}
