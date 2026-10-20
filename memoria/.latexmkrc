# Compilación: latexmk  (usa XeLaTeX, biber y genera la lista de acrónimos)
$pdf_mode = 5;  # XeLaTeX
add_cus_dep('nlo', 'nls', 0, 'makenlo2nls');
sub makenlo2nls {
    system("makeindex -s nomencl.ist -o \"$_[0].nls\" \"$_[0].nlo\"");
}
