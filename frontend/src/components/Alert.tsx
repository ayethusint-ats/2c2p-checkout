interface Props {
  message: string;
}

// role="alert" makes screen readers announce the message as soon as it appears.
export default function Alert({ message }: Props) {
  return (
    <p role="alert" className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-800">
      {message}
    </p>
  );
}
