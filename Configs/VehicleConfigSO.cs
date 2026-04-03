using UnityEngine;

namespace Odal.Configs
{
    /// <summary>
    /// Конфигурация характеристик машины (квадроцикла), хранимая в ассете.
    /// Позволяет регулировать баланс физики без изменения кода.
    /// </summary>
    [CreateAssetMenu(fileName = "New Vehicle Config", menuName = "Odal/Configs/Vehicle Config", order = 51)]
    public class VehicleConfigSO : ScriptableObject
    {
        [Header("Базовые параметры")]
        [Tooltip("Масса машины. Влияет на инерцию и силу физических воздействий.")]
        [Min(1f)]
        public float Mass = 1500f;

        [Header("Параметры пружинистой подвески")]
        [Tooltip("Жесткость пружины (k). Сила отталкивания сферы от земли.")]
        [Min(0f)]
        public float SpringStiffness = 300000f;

        [Tooltip("Коэффициент демпфирования (c). Влияет на скорость угасания подскоков квадроцикла.")]
        [Min(0f)]
        public float DampingCoefficient = 15000f;

        [Header("Геометрия шасси")]
        [Tooltip("Максимальная длина луча подвески (L_max / клиренс).")]
        [Min(0f)]
        public float SuspensionHeight = 1.0f;

        [Header("Силы управления")]
        [Tooltip("Сила продольной тяги (N). Применяется при нажатии газа.")]
        [Min(0f)]
        public float ThrustForce = 15000f;

        [Tooltip("Сила буста (N). Применяется при резком свайпе вверх.")]
        [Min(0f)]
        public float BoostForce = 45000f;

        [Tooltip("Сила прыжка (N). Применяется при отпускании прелоада (максимальное значение).")]
        [Min(0f)]
        public float JumpForce = 50000f;

        [Tooltip("Минимальная скорость (м/с) для срабатывания прыжка. Ниже этой скорости прелоад не сработает.")]
        [Min(0f)]
        public float MinJumpSpeed = 5f;

        [Tooltip("Скорость (м/с), при которой шаг прыжка достигает 100% своей силы.")]
        [Min(1f)]
        public float MaxJumpSpeed = 25f;

        [Tooltip("Сила торможения (N). Применяется при мульти-тапе.")]
        [Min(0f)]
        public float BrakeForce = 12000f;

        [Tooltip("Время (сек), которое нужно удерживать тормоз после полной остановки, чтобы автоматически включился газ.")]
        [Min(0f)]
        public float AutoGasDelayAfterStop = 0.5f;

        [Tooltip("Максимальная скорость поворота (°/сек). 60 = комфортно, 120 = резко.")]
        [Min(1f)]
        public float MaxSteeringSpeed = 60f;
    }
}
