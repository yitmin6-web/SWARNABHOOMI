import { motion, useReducedMotion } from 'motion/react'

export default function DecorativeLeaf() {
  const reduceMotion = useReducedMotion()

  return (
    <motion.div
      className="decorative-leaf"
      aria-hidden="true"
      animate={reduceMotion ? {} : { rotate: [0, 4, -3, 0], y: [0, -3, 1, 0] }}
      transition={reduceMotion ? {} : {
        duration: 7,
        repeat: Infinity,
        ease: 'easeInOut',
      }}
    />
  )
}
